# JD Mapping

| JD Requirement | Repo Evidence |
| --- | --- |
| C#/.NET backend systems and APIs | `ContentId.Api` exposes submission, status, match, health, and metrics endpoints. |
| Go working knowledge | `content-match-worker-go` processes identification jobs asynchronously. |
| MongoDB | Stores fingerprint documents and raw match documents. |
| SQL Server / relational DB | Stores normalized submission state and match summaries. |
| AWS SQS/SNS/S3 | LocalStack provisions SQS queue, DLQ, SNS topic, and S3 scratch bucket. |
| OpenTofu/Terraform | `infra/opentofu` defines representative AWS resources. |
| Docker Compose | Full local stack runs through `docker-compose.yml`. |
| OpenTelemetry | API and worker emit traces to the collector. |
| Python automation | `scripts/healthcheck.py` validates the end-to-end flow. |
| Troubleshooting and production readiness | `docs/runbook.md` covers startup, DLQ inspection, replay, and failure modes. |
| Developer docs | README plus architecture, runbook, JD mapping, and talking points. |
| Media/content domain familiarity | Seeded reference assets, fingerprint hashes, match results, and rights policies model content identification. |
