package main

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"log/slog"
	"math"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/aws/aws-sdk-go-v2/aws"
	"github.com/aws/aws-sdk-go-v2/config"
	"github.com/aws/aws-sdk-go-v2/credentials"
	"github.com/aws/aws-sdk-go-v2/service/sqs"
	_ "github.com/lib/pq"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"go.mongodb.org/mongo-driver/mongo/options"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	semconv "go.opentelemetry.io/otel/semconv/v1.27.0"
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

type identificationJobMessage struct {
	SubmissionID    string    `json:"submissionId"`
	FingerprintHash string    `json:"fingerprintHash"`
	EnqueuedAt      time.Time `json:"enqueuedAt"`
}

type referenceAsset struct {
	ReferenceAssetID string `json:"referenceAssetId"`
	Title            string `json:"title"`
	Owner            string `json:"owner"`
	FingerprintHash  string `json:"fingerprintHash"`
	RightsPolicy     string `json:"rightsPolicy"`
}

type matchResult struct {
	ReferenceAssetID string  `bson:"referenceAssetId"`
	Title            string  `bson:"title"`
	Owner            string  `bson:"owner"`
	RightsPolicy     string  `bson:"rightsPolicy"`
	Confidence       float64 `bson:"confidence"`
	FingerprintHash  string  `bson:"fingerprintHash"`
}

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	logger := slog.New(slog.NewJSONHandler(os.Stdout, nil))
	shutdown, err := configureTracing(ctx)
	if err != nil {
		logger.Warn("tracing disabled", "error", err)
	} else {
		defer shutdown(context.Background())
	}

	cfg := loadConfig()
	assets, err := loadReferenceAssets(cfg.ReferenceAssetsPath)
	if err != nil {
		logger.Error("failed to load reference assets", "error", err)
		os.Exit(1)
	}

	db, err := sql.Open("postgres", cfg.PostgresConnectionString)
	if err != nil {
		logger.Error("failed to open postgres", "error", err)
		os.Exit(1)
	}
	defer db.Close()
	if err := db.PingContext(ctx); err != nil {
		logger.Error("failed to ping postgres", "error", err)
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

			if err := processMessage(ctx, logger, db, mongoClient, cfg, assets, *message.Body); err != nil {
				logger.Error("job failed; message will be retried by sqs", "error", err)
				continue
			}

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

type appConfig struct {
	PostgresConnectionString string
	MongoConnectionString    string
	MongoDatabase            string
	QueueName                string
	AwsServiceURL            string
	AwsRegion                string
	ReferenceAssetsPath      string
}

func loadConfig() appConfig {
	return appConfig{
		PostgresConnectionString: env("POSTGRES_CONNECTION_STRING", "postgres://contentid:contentid@localhost:5432/contentid?sslmode=disable"),
		MongoConnectionString:    env("MONGO_CONNECTION_STRING", "mongodb://localhost:27017"),
		MongoDatabase:            env("MONGO_DATABASE", "contentid"),
		QueueName:                env("QUEUE_NAME", "content-id-job-queue"),
		AwsServiceURL:            env("AWS_SERVICE_URL", "http://localhost:4566"),
		AwsRegion:                env("AWS_REGION", "us-east-1"),
		ReferenceAssetsPath:      env("REFERENCE_ASSETS_PATH", "/app/data/reference-assets/reference-assets.json"),
	}
}

func env(key, fallback string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return fallback
}

func loadReferenceAssets(path string) ([]referenceAsset, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}

	var assets []referenceAsset
	if err := json.Unmarshal(content, &assets); err != nil {
		return nil, err
	}

	return assets, nil
}

func configureSQS(ctx context.Context, cfg appConfig) (*sqs.Client, string, error) {
	awsCfg, err := config.LoadDefaultConfig(ctx,
		config.WithRegion(cfg.AwsRegion),
		config.WithCredentialsProvider(credentials.NewStaticCredentialsProvider("test", "test", "")),
	)
	if err != nil {
		return nil, "", err
	}

	client := sqs.NewFromConfig(awsCfg, func(options *sqs.Options) {
		options.BaseEndpoint = aws.String(cfg.AwsServiceURL)
	})

	queue, err := client.GetQueueUrl(ctx, &sqs.GetQueueUrlInput{QueueName: aws.String(cfg.QueueName)})
	if err != nil {
		return nil, "", err
	}

	return client, *queue.QueueUrl, nil
}

func processMessage(
	ctx context.Context,
	logger *slog.Logger,
	db *sql.DB,
	mongoClient *mongo.Client,
	cfg appConfig,
	assets []referenceAsset,
	body string,
) error {
	tracer := otel.Tracer("content-match-worker-go")
	ctx, span := tracer.Start(ctx, "worker.process_identification_job")
	defer span.End()

	var message identificationJobMessage
	if err := json.Unmarshal([]byte(body), &message); err != nil {
		return err
	}
	if message.SubmissionID == "" {
		return errors.New("missing submissionId")
	}
	logger = logger.With("submission_id", message.SubmissionID)
	span.SetAttributes(
		attribute.String("submission.id", message.SubmissionID),
		attribute.String("fingerprint.hash", strings.ToLower(message.FingerprintHash)),
	)

	queryCtx, cancel := context.WithTimeout(ctx, queryTimeout)
	defer cancel()
	if _, err := db.ExecContext(queryCtx,
		"update submissions set status = 'processing', updated_at = now() where submission_id = $1",
		message.SubmissionID); err != nil {
		return err
	}

	matches := findMatches(message.FingerprintHash, assets)
	status := "no_match"
	if len(matches) > 0 {
		status = "matched"
	}

	if err := writeMatches(ctx, db, message.SubmissionID, matches); err != nil {
		return err
	}

	statusCtx, statusCancel := context.WithTimeout(ctx, queryTimeout)
	defer statusCancel()
	if _, err := db.ExecContext(statusCtx,
		"update submissions set status = $1, updated_at = now() where submission_id = $2",
		status, message.SubmissionID); err != nil {
		return err
	}

	mongoCtx, mongoCancel := context.WithTimeout(ctx, queryTimeout)
	defer mongoCancel()
	collection := mongoClient.Database(cfg.MongoDatabase).Collection("match_documents")
	_, err := collection.InsertOne(mongoCtx, bson.M{
		"submissionId":    message.SubmissionID,
		"fingerprintHash": strings.ToLower(message.FingerprintHash),
		"status":          status,
		"matches":         matches,
		"processedAt":     time.Now().UTC(),
	})
	if err != nil {
		return err
	}

	logger.Info("processed identification job", "submissionId", message.SubmissionID, "status", status, "matches", len(matches))
	return nil
}

func findMatches(fingerprintHash string, assets []referenceAsset) []matchResult {
	normalized := strings.ToLower(strings.TrimSpace(fingerprintHash))
	results := make([]matchResult, 0)
	for _, asset := range assets {
		if strings.ToLower(asset.FingerprintHash) == normalized {
			results = append(results, matchResult{
				ReferenceAssetID: asset.ReferenceAssetID,
				Title:            asset.Title,
				Owner:            asset.Owner,
				RightsPolicy:     asset.RightsPolicy,
				Confidence:       exactMatchConfidence,
				FingerprintHash:  asset.FingerprintHash,
			})
			continue
		}

		if prefixSimilarity(normalized, strings.ToLower(asset.FingerprintHash)) >= fuzzyPrefixThreshold {
			results = append(results, matchResult{
				ReferenceAssetID: asset.ReferenceAssetID,
				Title:            asset.Title,
				Owner:            asset.Owner,
				RightsPolicy:     asset.RightsPolicy,
				Confidence:       fuzzyMatchConfidence,
				FingerprintHash:  asset.FingerprintHash,
			})
		}
	}

	return results
}

func prefixSimilarity(left, right string) float64 {
	if left == "" || right == "" {
		return 0
	}
	maxLength := math.Max(float64(len(left)), float64(len(right)))
	matches := 0
	for i := 0; i < len(left) && i < len(right); i++ {
		if left[i] != right[i] {
			break
		}
		matches++
	}
	return float64(matches) / maxLength
}

func writeMatches(ctx context.Context, db *sql.DB, submissionID string, matches []matchResult) error {
	for _, match := range matches {
		queryCtx, cancel := context.WithTimeout(ctx, queryTimeout)
		defer cancel()
		_, err := db.ExecContext(queryCtx,
			`insert into match_results (
				submission_id, reference_asset_id, title, owner, rights_policy, confidence, fingerprint_hash)
			 values ($1, $2, $3, $4, $5, $6, $7)
			 on conflict (submission_id, reference_asset_id)
			 do update set
				title = excluded.title,
				owner = excluded.owner,
				rights_policy = excluded.rights_policy,
				confidence = excluded.confidence,
				fingerprint_hash = excluded.fingerprint_hash`,
			submissionID,
			match.ReferenceAssetID,
			match.Title,
			match.Owner,
			match.RightsPolicy,
			match.Confidence,
			match.FingerprintHash)
		if err != nil {
			return err
		}
	}
	return nil
}

func configureTracing(ctx context.Context) (func(context.Context) error, error) {
	exporter, err := otlptracegrpc.New(ctx)
	if err != nil {
		return nil, err
	}

	provider := sdktrace.NewTracerProvider(
		sdktrace.WithBatcher(exporter),
		sdktrace.WithResource(resource.NewWithAttributes(
			semconv.SchemaURL,
			semconv.ServiceName("content-match-worker-go"),
		)),
	)
	otel.SetTracerProvider(provider)
	otel.SetTextMapPropagator(propagation.TraceContext{})

	return provider.Shutdown, nil
}
