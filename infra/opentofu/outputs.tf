output "job_queue_url" {
  value       = aws_sqs_queue.content_id_job_queue.url
  description = "Queue URL for content identification jobs."
}

output "job_dlq_url" {
  value       = aws_sqs_queue.content_id_job_dlq.url
  description = "Queue URL for failed jobs after retry exhaustion."
}

output "events_topic_arn" {
  value       = aws_sns_topic.content_id_events.arn
  description = "SNS topic ARN for content identification events."
}

output "media_scratch_bucket" {
  value       = aws_s3_bucket.media_scratch.bucket
  description = "Scratch bucket for media/object payloads."
}
