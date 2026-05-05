#!/usr/bin/env bash
set -euo pipefail

awslocal sqs create-queue --queue-name content-id-job-dlq >/tmp/content-id-dlq.json
DLQ_ARN="$(awslocal sqs get-queue-attributes \
  --queue-url http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/content-id-job-dlq \
  --attribute-names QueueArn \
  --query 'Attributes.QueueArn' \
  --output text)"

awslocal sqs create-queue \
  --queue-name content-id-job-queue \
  --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"${DLQ_ARN}\\\",\\\"maxReceiveCount\\\":\\\"3\\\"}\"}"

awslocal sns create-topic --name content-id-events
awslocal s3 mb s3://content-id-media-scratch

echo "LocalStack content-id resources are ready."
