FROM golang:1.23 AS build
WORKDIR /src
COPY src/content-match-worker-go/go.mod src/content-match-worker-go/
WORKDIR /src/src/content-match-worker-go
RUN go mod download
WORKDIR /src
COPY . .
WORKDIR /src/src/content-match-worker-go
RUN CGO_ENABLED=0 GOOS=linux go build -buildvcs=false -o /out/content-match-worker .

FROM gcr.io/distroless/base-debian12:nonroot
WORKDIR /app
COPY --from=build /out/content-match-worker /app/content-match-worker
COPY data/reference-assets /app/data/reference-assets
ENTRYPOINT ["/app/content-match-worker"]
