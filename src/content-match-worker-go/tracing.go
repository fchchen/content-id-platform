package main

import (
	"context"

	"go.opentelemetry.io/contrib/bridges/otelslog"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploggrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/log/global"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/sdk/resource"
	sdklog "go.opentelemetry.io/otel/sdk/log"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	semconv "go.opentelemetry.io/otel/semconv/v1.27.0"
	"log/slog"
)

var workerResource = resource.NewWithAttributes(
	semconv.SchemaURL,
	semconv.ServiceName("content-match-worker-go"),
)

func configureTracing(ctx context.Context) (func(context.Context) error, error) {
	exporter, err := otlptracegrpc.New(ctx)
	if err != nil {
		return nil, err
	}

	provider := sdktrace.NewTracerProvider(
		sdktrace.WithBatcher(exporter),
		sdktrace.WithResource(workerResource),
	)
	otel.SetTracerProvider(provider)
	otel.SetTextMapPropagator(propagation.TraceContext{})

	return provider.Shutdown, nil
}

func configureLogging(ctx context.Context) (*slog.Logger, func(context.Context) error, error) {
	exporter, err := otlploggrpc.New(ctx)
	if err != nil {
		return nil, nil, err
	}

	provider := sdklog.NewLoggerProvider(
		sdklog.WithProcessor(sdklog.NewBatchProcessor(exporter)),
		sdklog.WithResource(workerResource),
	)
	global.SetLoggerProvider(provider)

	logger := slog.New(otelslog.NewHandler("content-match-worker-go"))
	return logger, provider.Shutdown, nil
}
