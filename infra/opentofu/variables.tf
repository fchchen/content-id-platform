variable "aws_region" {
  type        = string
  description = "AWS region for the content identification platform."
  default     = "us-east-1"
}

variable "environment" {
  type        = string
  description = "Deployment environment name."
  default     = "demo"
}

variable "service_name" {
  type        = string
  description = "Base service name used in resource names."
  default     = "content-id-platform"
}
