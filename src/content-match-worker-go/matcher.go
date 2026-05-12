package main

import (
	"encoding/json"
	"math"
	"os"
	"strings"
	"time"
)

type identificationJobMessage struct {
	SubmissionID    string    `json:"submissionId"`
	FingerprintHash string    `json:"fingerprintHash"`
	EnqueuedAt      time.Time `json:"enqueuedAt"`
}

type referenceAsset struct {
	ReferenceAssetID string `json:"referenceAssetId"`
	Title            string `json:"title"`
	Owner            string `json:"owner"`
	FingerprintHash  string `json:"fingerprintHash"`
	RightsPolicy     string `json:"rightsPolicy"`
}

type matchResult struct {
	ReferenceAssetID string  `bson:"referenceAssetId"`
	Title            string  `bson:"title"`
	Owner            string  `bson:"owner"`
	RightsPolicy     string  `bson:"rightsPolicy"`
	Confidence       float64 `bson:"confidence"`
	FingerprintHash  string  `bson:"fingerprintHash"`
}

func loadReferenceAssets(path string) ([]referenceAsset, error) {
	content, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}

	var assets []referenceAsset
	if err := json.Unmarshal(content, &assets); err != nil {
		return nil, err
	}

	return assets, nil
}

func findMatches(fingerprintHash string, assets []referenceAsset) []matchResult {
	normalized := strings.ToLower(strings.TrimSpace(fingerprintHash))
	results := make([]matchResult, 0)
	for _, asset := range assets {
		if strings.ToLower(asset.FingerprintHash) == normalized {
			results = append(results, matchResult{
				ReferenceAssetID: asset.ReferenceAssetID,
				Title:            asset.Title,
				Owner:            asset.Owner,
				RightsPolicy:     asset.RightsPolicy,
				Confidence:       exactMatchConfidence,
				FingerprintHash:  asset.FingerprintHash,
			})
			continue
		}

		if prefixSimilarity(normalized, strings.ToLower(asset.FingerprintHash)) >= fuzzyPrefixThreshold {
			results = append(results, matchResult{
				ReferenceAssetID: asset.ReferenceAssetID,
				Title:            asset.Title,
				Owner:            asset.Owner,
				RightsPolicy:     asset.RightsPolicy,
				Confidence:       fuzzyMatchConfidence,
				FingerprintHash:  asset.FingerprintHash,
			})
		}
	}

	return results
}

func prefixSimilarity(left, right string) float64 {
	if left == "" || right == "" {
		return 0
	}
	maxLength := math.Max(float64(len(left)), float64(len(right)))
	matches := 0
	for i := 0; i < len(left) && i < len(right); i++ {
		if left[i] != right[i] {
			break
		}
		matches++
	}
	return float64(matches) / maxLength
}
