#!/usr/bin/env bash
set -euo pipefail

DLQ_URL="$(awslocal sqs create-queue \
  --queue-name content-id-job-dlq \
  --query 'QueueUrl' \
  --output text)"
DLQ_ARN="$(awslocal sqs get-queue-attributes \
  --queue-url "${DLQ_URL}" \
  --attribute-names QueueArn \
  --query 'Attributes.QueueArn' \
  --output text)"

awslocal sqs create-queue \
  --queue-name content-id-job-queue \
  --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"${DLQ_ARN}\\\",\\\"maxReceiveCount\\\":\\\"3\\\"}\"}"

awslocal sns create-topic --name content-id-events
awslocal s3 mb s3://content-id-media-scratch

echo "LocalStack content-id resources are ready."
