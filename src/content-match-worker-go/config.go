package main

import "os"

type appConfig struct {
	SqlServerConnectionString string
	MongoConnectionString     string
	MongoDatabase             string
	QueueName                 string
	AwsServiceURL             string
	AwsRegion                 string
	ReferenceAssetsPath       string
}

func loadConfig() appConfig {
	return appConfig{
		SqlServerConnectionString: env("SQLSERVER_CONNECTION_STRING", "sqlserver://sa:ContentId_local_2026!@localhost:11433?database=contentid&encrypt=true&TrustServerCertificate=true"),
		MongoConnectionString:     env("MONGO_CONNECTION_STRING", "mongodb://localhost:27017"),
		MongoDatabase:             env("MONGO_DATABASE", "contentid"),
		QueueName:                 env("QUEUE_NAME", "content-id-job-queue"),
		AwsServiceURL:             env("AWS_SERVICE_URL", "http://localhost:4566"),
		AwsRegion:                 env("AWS_REGION", "us-east-1"),
		ReferenceAssetsPath:       env("REFERENCE_ASSETS_PATH", "/app/data/reference-assets/reference-assets.json"),
	}
}

func env(key, fallback string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return fallback
}
