package main

import (
	"context"
	"database/sql"
	"log/slog"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/aws/aws-sdk-go-v2/aws"
	"github.com/aws/aws-sdk-go-v2/service/sqs"
	_ "github.com/denisenkom/go-mssqldb"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
)

const (
	sqsMaxMessages       = int32(5)
	sqsLongPollSeconds   = int32(10)
	sqsVisibilitySeconds = int32(30)
	receiveBackoff       = 2 * time.Second
	queryTimeout         = 5 * time.Second

	exactMatchConfidence = 0.99
	fuzzyMatchConfidence = 0.72
	// The demo fuzzy matcher uses a conservative shared-prefix floor to show near-match behavior
	// without pretending to be a real audio fingerprinting algorithm.
	fuzzyPrefixThreshold = 0.65
)

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	logger, shutdownLogs, err := configureLogging(ctx)
	if err != nil {
		logger = slog.New(slog.NewJSONHandler(os.Stdout, nil))
		logger.Warn("otel logging disabled, falling back to stdout", "error", err)
	} else {
		defer shutdownLogs(context.Background())
	}

	shutdown, err := configureTracing(ctx)
	if err != nil {
		logger.Warn("tracing disabled", "error", err)
	} else {
		defer shutdown(context.Background())
	}

	shutdownMetrics, err := configureMetrics(ctx)
	if err != nil {
		logger.Warn("metrics disabled", "error", err)
	} else {
		defer shutdownMetrics(context.Background())
	}

	meter := otel.Meter("content-match-worker-go")
	jobsProcessed, _ := meter.Int64Counter("worker.jobs.processed",
		metric.WithDescription("Identification jobs processed by the worker"))

	cfg := loadConfig()
	assets, err := loadReferenceAssets(cfg.ReferenceAssetsPath)
	if err != nil {
		logger.Error("failed to load reference assets", "error", err)
		os.Exit(1)
	}

	db, err := sql.Open("sqlserver", cfg.SqlServerConnectionString)
	if err != nil {
		logger.Error("failed to open sql server", "error", err)
		os.Exit(1)
	}
	defer db.Close()
	if err := db.PingContext(ctx); err != nil {
		logger.Error("failed to ping sql server", "error", err)
		os.Exit(1)
	}

	mongoClient, err := mongo.Connect(ctx, options.Client().ApplyURI(cfg.MongoConnectionString))
	if err != nil {
		logger.Error("failed to connect mongo", "error", err)
		os.Exit(1)
	}
	defer mongoClient.Disconnect(context.Background())

	sqsClient, queueURL, err := configureSQS(ctx, cfg)
	if err != nil {
		logger.Error("failed to configure sqs", "error", err)
		os.Exit(1)
	}

	logger.Info("content match worker started", "queue", queueURL, "referenceAssets", len(assets))
	for ctx.Err() == nil {
		output, err := sqsClient.ReceiveMessage(ctx, &sqs.ReceiveMessageInput{
			QueueUrl:            aws.String(queueURL),
			MaxNumberOfMessages: sqsMaxMessages,
			WaitTimeSeconds:     sqsLongPollSeconds,
			VisibilityTimeout:   sqsVisibilitySeconds,
		})
		if err != nil {
			logger.Warn("receive failed", "error", err)
			time.Sleep(receiveBackoff)
			continue
		}

		for _, message := range output.Messages {
			if message.Body == nil || message.ReceiptHandle == nil {
				continue
			}

			result, err := processMessage(ctx, logger, db, mongoClient, cfg, assets, *message.Body)
			if err != nil {
				jobsProcessed.Add(ctx, 1, metric.WithAttributes(attribute.String("result", "error")))
				logger.Error("job failed; message will be retried by sqs", "error", err)
				continue
			}
			jobsProcessed.Add(ctx, 1, metric.WithAttributes(attribute.String("result", result)))

			_, err = sqsClient.DeleteMessage(ctx, &sqs.DeleteMessageInput{
				QueueUrl:      aws.String(queueURL),
				ReceiptHandle: message.ReceiptHandle,
			})
			if err != nil {
				logger.Warn("failed to delete processed message", "error", err)
			}
		}
	}
}
