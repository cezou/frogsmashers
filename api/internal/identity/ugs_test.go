package identity

import (
	"context"
	"crypto/rand"
	"crypto/rsa"
	"encoding/base64"
	"errors"
	"fmt"
	"math/big"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

const (
	testKid       = "test-key"
	testIssuer    = "https://player-auth.example.test"
	testProjectID = "f5191c3e-9214-473c-8783-94477861bd6c"
	testPlayerID  = "player-abc-123"
)

// newJWKSServer serves a JWKS containing the RSA public key, exactly
// what keyfunc fetches from Unity in production.
func newJWKSServer(t *testing.T, pub *rsa.PublicKey) *httptest.Server {
	t.Helper()
	n := base64.RawURLEncoding.EncodeToString(pub.N.Bytes())
	e := base64.RawURLEncoding.EncodeToString(
		big.NewInt(int64(pub.E)).Bytes())
	jwks := fmt.Sprintf(`{"keys":[{"kty":"RSA","kid":"%s",`+
		`"use":"sig","alg":"RS256","n":"%s","e":"%s"}]}`,
		testKid, n, e)
	srv := httptest.NewServer(http.HandlerFunc(
		func(w http.ResponseWriter, r *http.Request) {
			w.Header().Set("Content-Type", "application/json")
			w.Write([]byte(jwks))
		}))
	t.Cleanup(srv.Close)
	return srv
}

func mintToken(
	t *testing.T, key *rsa.PrivateKey, kid string, claims jwt.MapClaims,
) string {
	t.Helper()
	token := jwt.NewWithClaims(jwt.SigningMethodRS256, claims)
	token.Header["kid"] = kid
	signed, err := token.SignedString(key)
	if err != nil {
		t.Fatalf("sign token: %v", err)
	}
	return signed
}

func validClaims() jwt.MapClaims {
	now := time.Now()
	return jwt.MapClaims{
		"iss": testIssuer,
		"aud": []string{"upid:" + testProjectID, "envName:production"},
		"sub": testPlayerID,
		"iat": now.Unix(),
		"exp": now.Add(time.Hour).Unix(),
	}
}

func newTestProvider(t *testing.T, key *rsa.PrivateKey) *UGSProvider {
	t.Helper()
	srv := newJWKSServer(t, &key.PublicKey)
	p, err := NewUGSProvider(context.Background(),
		srv.URL, testIssuer, testProjectID)
	if err != nil {
		t.Fatalf("NewUGSProvider: %v", err)
	}
	return p
}

func testKey(t *testing.T) *rsa.PrivateKey {
	t.Helper()
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatalf("generate key: %v", err)
	}
	return key
}

func TestUGSVerifyValidToken(t *testing.T) {
	key := testKey(t)
	p := newTestProvider(t, key)

	token := mintToken(t, key, testKid, validClaims())
	id, err := p.VerifyCredential(context.Background(), token)
	if err != nil {
		t.Fatalf("VerifyCredential: %v", err)
	}
	if id != testPlayerID {
		t.Fatalf("provider id = %q, want %q", id, testPlayerID)
	}
}

func TestUGSVerifyBareAudience(t *testing.T) {
	key := testKey(t)
	p := newTestProvider(t, key)

	claims := validClaims()
	claims["aud"] = testProjectID
	token := mintToken(t, key, testKid, claims)
	if _, err := p.VerifyCredential(
		context.Background(), token); err != nil {
		t.Fatalf("bare audience should be accepted: %v", err)
	}
}

func TestUGSVerifyRejections(t *testing.T) {
	key := testKey(t)
	p := newTestProvider(t, key)

	expired := validClaims()
	expired["exp"] = time.Now().Add(-time.Hour).Unix()

	wrongAud := validClaims()
	wrongAud["aud"] = []string{"upid:other-project"}

	wrongIss := validClaims()
	wrongIss["iss"] = "https://evil.example.test"

	noSub := validClaims()
	delete(noSub, "sub")

	otherKey := testKey(t)

	cases := map[string]string{
		"expired":     mintToken(t, key, testKid, expired),
		"wrong aud":   mintToken(t, key, testKid, wrongAud),
		"wrong iss":   mintToken(t, key, testKid, wrongIss),
		"missing sub": mintToken(t, key, testKid, noSub),
		"unknown kid": mintToken(t, key, "other-kid", validClaims()),
		"wrong key":   mintToken(t, otherKey, testKid, validClaims()),
		"garbage":     "not-a-jwt",
	}
	for name, token := range cases {
		_, err := p.VerifyCredential(context.Background(), token)
		if !errors.Is(err, ErrInvalidCredential) {
			t.Errorf("%s: err = %v, want ErrInvalidCredential",
				name, err)
		}
	}
}
