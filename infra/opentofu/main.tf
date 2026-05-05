locals {
  name_prefix = "${var.service_name}-${var.environment}"

  tags = {
    Application = var.service_name
    Environment = var.environment
    ManagedBy   = "opentofu"
  }
}

resource "aws_sqs_queue" "content_id_job_dlq" {
  name                      = "${local.name_prefix}-job-dlq"
  message_retention_seconds = 1209600
  tags                      = local.tags
}

resource "aws_sqs_queue" "content_id_job_queue" {
  name                       = "${local.name_prefix}-job-queue"
  visibility_timeout_seconds = 30
  message_retention_seconds  = 345600

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.content_id_job_dlq.arn
    maxReceiveCount     = 3
  })

  tags = local.tags
}

resource "aws_sns_topic" "content_id_events" {
  name = "${local.name_prefix}-events"
  tags = local.tags
}

resource "aws_s3_bucket" "media_scratch" {
  bucket = "${local.name_prefix}-media-scratch"
  tags   = local.tags
}

resource "aws_s3_bucket_public_access_block" "media_scratch" {
  bucket                  = aws_s3_bucket.media_scratch.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "media_scratch" {
  bucket = aws_s3_bucket.media_scratch.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_versioning" "media_scratch" {
  bucket = aws_s3_bucket.media_scratch.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_cloudwatch_log_group" "api" {
  name              = "/aws/ecs/${local.name_prefix}/api"
  retention_in_days = 30
  tags              = local.tags
}

resource "aws_cloudwatch_log_group" "worker" {
  name              = "/aws/ecs/${local.name_prefix}/worker"
  retention_in_days = 30
  tags              = local.tags
}

resource "aws_iam_policy" "runtime" {
  name        = "${local.name_prefix}-runtime"
  description = "Minimum runtime permissions for the content identification API and worker."

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "sqs:GetQueueUrl",
          "sqs:SendMessage",
          "sqs:ReceiveMessage",
          "sqs:DeleteMessage",
          "sqs:GetQueueAttributes"
        ]
        Resource = [
          aws_sqs_queue.content_id_job_queue.arn,
          aws_sqs_queue.content_id_job_dlq.arn
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "sns:Publish"
        ]
        Resource = aws_sns_topic.content_id_events.arn
      },
      {
        Effect = "Allow"
        Action = [
          "s3:GetObject",
          "s3:PutObject"
        ]
        Resource = "${aws_s3_bucket.media_scratch.arn}/*"
      }
    ]
  })

  tags = local.tags
}
