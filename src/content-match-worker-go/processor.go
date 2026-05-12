package main

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"log/slog"
	"strings"
	"time"

	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
)

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
		"update submissions set status = 'processing', updated_at = sysutcdatetime() where submission_id = @p1",
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
		"update submissions set status = @p1, updated_at = sysutcdatetime() where submission_id = @p2",
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

func writeMatches(ctx context.Context, db *sql.DB, submissionID string, matches []matchResult) error {
	for _, match := range matches {
		queryCtx, cancel := context.WithTimeout(ctx, queryTimeout)
		_, err := db.ExecContext(queryCtx,
			`merge match_results with (holdlock) as target
			 using (values (@p1, @p2, @p3, @p4, @p5, @p6, @p7)) as source (
				submission_id, reference_asset_id, title, owner, rights_policy, confidence, fingerprint_hash)
			 on target.submission_id = source.submission_id
				and target.reference_asset_id = source.reference_asset_id
			 when matched then update set
				title = source.title,
				owner = source.owner,
				rights_policy = source.rights_policy,
				confidence = source.confidence,
				fingerprint_hash = source.fingerprint_hash
			 when not matched then insert (
				submission_id, reference_asset_id, title, owner, rights_policy, confidence, fingerprint_hash)
			 values (
				source.submission_id, source.reference_asset_id, source.title, source.owner,
				source.rights_policy, source.confidence, source.fingerprint_hash);`,
			submissionID,
			match.ReferenceAssetID,
			match.Title,
			match.Owner,
			match.RightsPolicy,
			match.Confidence,
			match.FingerprintHash)
		cancel()
		if err != nil {
			return err
		}
	}
	return nil
}
