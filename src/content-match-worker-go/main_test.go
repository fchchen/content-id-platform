package main

import "testing"

func TestFindMatchesExactMatch(t *testing.T) {
	assets := []referenceAsset{{
		ReferenceAssetID: "ref-001",
		Title:            "Sample Song A",
		Owner:            "Example Music Group",
		FingerprintHash:  "abc123",
		RightsPolicy:     "monetize",
	}}

	matches := findMatches("ABC123", assets)

	if len(matches) != 1 {
		t.Fatalf("expected one match, got %d", len(matches))
	}
	if matches[0].Confidence != exactMatchConfidence {
		t.Fatalf("expected confidence %.2f, got %.2f", exactMatchConfidence, matches[0].Confidence)
	}
}

func TestFindMatchesNoMatch(t *testing.T) {
	assets := []referenceAsset{{
		ReferenceAssetID: "ref-001",
		FingerprintHash:  "abc123",
	}}

	matches := findMatches("nomatch", assets)

	if len(matches) != 0 {
		t.Fatalf("expected no matches, got %d", len(matches))
	}
}

func TestFindMatchesFuzzyPrefixBoundary(t *testing.T) {
	assets := []referenceAsset{{
		ReferenceAssetID: "ref-001",
		Title:            "Sample Song A",
		Owner:            "Example Music Group",
		FingerprintHash:  "abcdef",
		RightsPolicy:     "track",
	}}

	matches := findMatches("abcdzz", assets)

	if len(matches) != 1 {
		t.Fatalf("expected fuzzy match at prefix boundary, got %d", len(matches))
	}
	if matches[0].Confidence != fuzzyMatchConfidence {
		t.Fatalf("expected confidence %.2f, got %.2f", fuzzyMatchConfidence, matches[0].Confidence)
	}
}
