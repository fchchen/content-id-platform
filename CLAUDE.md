# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Demo content identification platform simulating automatic content recognition (ACR). A client submits media metadata + a fingerprint hash → the C#/.NET API persists it and enqueues a job → a Go worker consumes the job, runs fingerprint matching against a reference catalog, and writes results back.

**Tech stack**: .NET 8 / ASP.NET Core minimal APIs · Go 1.23 · SQL Server 2022 · MongoDB 7 · AWS SQS (LocalStack) · OpenTelemetry · OpenTofu

## Commands

### Run the full stack

```bash
docker compose up --build        # Build both images and start all services
docker compose up --build -d     # Same but detached
docker compose logs -f contentid-api
docker compose logs -f content-match-worker-go
```

### End-to-end validation

```bash
python3 scripts/healthcheck.py   # Waits for API, submits fingerprint "abc123", polls for a match result
```

### .NET API

```bash
cd src/ContentId.Api
dotnet build
dotnet run                        # Dev mode — Swagger UI at http://localhost:5000/swagger

# Tests (WebApplicationFactory, no external deps needed)
dotnet test                       # Run all xUnit tests
dotnet test --filter "FullyQualifiedName~SubmissionApiTests.CreateSubmission"
```

### Go Worker

```bash
cd src/content-match-worker-go
go build ./...
go test ./...
go vet ./...
```

### Infrastructure (OpenTofu)

```bash
cd infra/opentofu
tofu init
tofu plan -var="service_name=content-id" -var="environment=dev"
tofu apply
```

## Architecture

### Request flow

```
Client
  │  POST /v1/submissions
  ▼
ContentId.Api (.NET)
  ├─ Validates input (content_type, duration, required fields)
  ├─ Inserts row into SQL Server submissions (status=queued)
  ├─ Inserts document into MongoDB fingerprint_documents
  └─ Publishes JSON job to SQS content-id-job-queue
              │
              ▼
content-match-worker-go (Go)
  ├─ Long-polls SQS (max 5 msgs, 10 s wait, 30 s visibility)
  ├─ Marks submission "processing" in SQL Server
  ├─ Matches fingerprint_hash against reference-assets.json
  │   └─ Exact match → confidence 0.99
  │   └─ Prefix similarity ≥ 0.65 → confidence 0.72
  ├─ Writes rows to SQL Server match_results (MERGE upsert)
  ├─ Writes document to MongoDB match_documents
  ├─ Updates submission status → "matched" | "no_match"
  └─ Deletes SQS message (failure = retry up to 3×, then DLQ)
```

Submission lifecycle states: `queued → processing → matched | no_match | failed`

### Key source files

| File | Role |
|------|------|
| `src/ContentId.Api/Program.cs` | **All** API logic: config (options pattern), models, service layer, repository/queue implementations |
| `src/content-match-worker-go/main.go` | **All** worker logic: SQS polling, matching algorithm, SQL Server + MongoDB writes |
| `docker-compose.yml` | Full local stack (sqlserver, mongo, localstack, otel-collector, api, worker) |
| `data/reference-assets/reference-assets.json` | Three seed fingerprints used by the worker for demo matching |
| `scripts/localstack-init.sh` | Creates SQS queues (job queue + DLQ with redrive policy) and SNS/S3 in LocalStack |
| `infra/opentofu/main.tf` | AWS resource definitions: SQS, SNS, S3, IAM, CloudWatch log groups |
| `observability/otel-collector-config.yaml` | OTLP receivers, debug exporter, Prometheus metrics export |

### Dependency injection / testing mode

`Program.cs` switches implementations based on `ASPNETCORE_ENVIRONMENT`:

- **Testing** → `InMemorySubmissionRepository`, `NoOpFingerprintDocumentStore`, `NoOpIdentificationQueue`
- **Development / Production** → SQL Server (`Microsoft.Data.SqlClient`), MongoDB driver, AWS SDK SQS

This is why `dotnet test` requires no running infrastructure.

### Storage

**SQL Server** (normalized state):
- `submissions` — canonical submission record + status
- `match_results` — per-asset match rows (UPSERT on re-run)

**MongoDB** (document store):
- `fingerprint_documents` — flexible submission doc written at creation
- `match_documents` — raw results doc written by worker after processing

### Observability

Both services export OpenTelemetry traces via gRPC to the OTel Collector (`:4317`). The collector re-exports to:
- **Debug exporter** (stdout) — visible in `docker compose logs otel-collector`
- **Prometheus** — scraped at `:8889/metrics`

The API also exposes `/health` (dependency check: SQL Server + MongoDB + asset catalog) and `/metrics` (Prometheus gauge stub).

## Non-obvious conventions

- **Fingerprint normalization**: The API lowercases `fingerprint_hash` on ingest; the worker trims + lowercases before comparison.
- **Reference asset path resolution**: The catalog loader tries the path as-is, then relative to the binary, then falls back — relevant when running outside Docker.
- **Port offsets in docker-compose**: External ports are prefixed with `1` to avoid conflicts (sqlserver→11433, mongo→27018, api→18080, otel gRPC→14317).
- **LocalStack credentials**: Hardcoded `test`/`test` AWS key/secret throughout — only valid for LocalStack.
- **Go worker shutdown**: Handles `SIGTERM` for graceful drain; in-flight message visibility timeout is 30 s.
- **`match_results` upsert**: Uses SQL Server `MERGE` on `(submission_id, reference_asset_id)` so re-processing a job is idempotent.
