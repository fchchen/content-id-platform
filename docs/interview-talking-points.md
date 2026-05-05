# Interview Talking Points

## Project Story

I built a simplified content identification platform inspired by the role. The API accepts media submissions, persists job state, queues identification work, and a Go worker processes the job asynchronously against seeded reference assets.

## Architecture Tradeoffs

- The .NET API owns platform and business workflow concerns.
- The Go worker represents the async matching path, where a real system might optimize for throughput and efficient processing.
- PostgreSQL keeps normalized operational state easy to query.
- MongoDB holds flexible fingerprint and raw match documents.
- SQS decouples submission intake from matching work.
- The DLQ gives a concrete production incident story for poison messages and replay.

## Production Readiness Discussion

The repo intentionally keeps real audio/video fingerprint extraction out of scope. In production, the matching component would use a proper fingerprinting engine, richer reference indexes, rate limiting, client auth, tenant isolation, autoscaling, and stronger secrets management.

## Strong Demo Path

1. Start the stack with Docker Compose.
2. Submit fingerprint `abc123`.
3. Show the API returns `queued`.
4. Show the worker transitions it to `matched`.
5. Retrieve the match result and rights policy.
6. Explain DLQ retry behavior and OpenTelemetry traces.
