package auth

import (
	"encoding/base64"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

var testSecret = []byte("0123456789abcdef0123456789abcdef")

func TestIssueVerifyRoundTrip(t *testing.T) {
	issuer := NewIssuer(testSecret, time.Hour)
	token, expiresAt, err := issuer.Issue(76561197960287930, "steam")
	if err != nil {
		t.Fatalf("Issue: %v", err)
	}
	if until := time.Until(expiresAt); until < 59*time.Minute {
		t.Fatalf("expiresAt too close: %v", until)
	}
	id, err := issuer.Verify(token)
	if err != nil {
		t.Fatalf("Verify: %v", err)
	}
	if id != 76561197960287930 {
		t.Fatalf("user id = %d", id)
	}
}

func TestVerifyRejections(t *testing.T) {
	issuer := NewIssuer(testSecret, time.Hour)
	token, _, _ := issuer.Issue(42, "ugs")

	expired, _, _ := NewIssuer(testSecret, -time.Hour).Issue(42, "ugs")
	otherKey, _, _ := NewIssuer(
		[]byte("another-secret-another-secret!!!"), time.Hour).
		Issue(42, "ugs")

	parts := strings.Split(token, ".")
	tamperedPayload := base64.RawURLEncoding.EncodeToString(
		[]byte(`{"sub":"1","iss":"frogsmashers","exp":99999999999}`))
	tampered := parts[0] + "." + tamperedPayload + "." + parts[2]

	noneHeader := base64.RawURLEncoding.EncodeToString(
		[]byte(`{"alg":"none","typ":"JWT"}`))
	noneToken := noneHeader + "." + parts[1] + "."

	cases := map[string]string{
		"expired":      expired,
		"wrong secret": otherKey,
		"tampered":     tampered,
		"alg none":     noneToken,
		"garbage":      "nope",
	}
	for name, tok := range cases {
		if _, err := issuer.Verify(tok); err == nil {
			t.Errorf("%s: Verify should fail", name)
		}
	}
}

func TestRequireAuthMiddleware(t *testing.T) {
	issuer := NewIssuer(testSecret, time.Hour)
	token, _, _ := issuer.Issue(1234, "ugs")

	var gotID int64
	var gotOK bool
	handler := RequireAuth(issuer)(http.HandlerFunc(
		func(w http.ResponseWriter, r *http.Request) {
			gotID, gotOK = UserID(r.Context())
		}))

	rec := httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodGet, "/me", nil)
	req.Header.Set("Authorization", "Bearer "+token)
	handler.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK || !gotOK || gotID != 1234 {
		t.Fatalf("authorized request: code=%d id=%d ok=%v",
			rec.Code, gotID, gotOK)
	}

	for name, header := range map[string]string{
		"missing":   "",
		"not a jwt": "Bearer nope",
		"no scheme": token,
	} {
		rec := httptest.NewRecorder()
		req := httptest.NewRequest(http.MethodGet, "/me", nil)
		if header != "" {
			req.Header.Set("Authorization", header)
		}
		handler.ServeHTTP(rec, req)
		if rec.Code != http.StatusUnauthorized {
			t.Errorf("%s: code = %d, want 401", name, rec.Code)
		}
	}
}
