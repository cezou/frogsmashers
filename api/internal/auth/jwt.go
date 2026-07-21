// Package auth issues and verifies the FrogSmashers JWT used as
// Bearer token by every authenticated endpoint (/queue,
// /match-result, ... later).
package auth

import (
	"context"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

const issuerName = "frogsmashers"

type Issuer struct {
	secret []byte
	ttl    time.Duration
}

func NewIssuer(secret []byte, ttl time.Duration) *Issuer {
	return &Issuer{secret: secret, ttl: ttl}
}

// Issue signs an HS256 token with sub = user id (decimal string, as
// steamid64 exceeds float64-safe integers) and a provider claim.
func (i *Issuer) Issue(
	userID int64, provider string,
) (string, time.Time, error) {
	now := time.Now()
	expiresAt := now.Add(i.ttl)
	claims := jwt.MapClaims{
		"sub":      strconv.FormatInt(userID, 10),
		"iss":      issuerName,
		"iat":      now.Unix(),
		"exp":      expiresAt.Unix(),
		"provider": provider,
	}
	token := jwt.NewWithClaims(jwt.SigningMethodHS256, claims)
	signed, err := token.SignedString(i.secret)
	return signed, expiresAt, err
}

// Verify validates signature, expiry and issuer, returning the user
// id carried in sub.
func (i *Issuer) Verify(token string) (int64, error) {
	claims := jwt.MapClaims{}
	_, err := jwt.ParseWithClaims(token, claims,
		func(*jwt.Token) (any, error) { return i.secret, nil },
		jwt.WithValidMethods([]string{"HS256"}),
		jwt.WithIssuer(issuerName),
		jwt.WithExpirationRequired(),
	)
	if err != nil {
		return 0, err
	}
	sub, err := claims.GetSubject()
	if err != nil {
		return 0, err
	}
	return strconv.ParseInt(sub, 10, 64)
}

type ctxKey struct{}

// RequireAuth is a chi-compatible middleware enforcing a valid
// Bearer token; the user id is stored in the request context.
func RequireAuth(i *Issuer) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		fn := func(w http.ResponseWriter, r *http.Request) {
			raw, ok := strings.CutPrefix(
				r.Header.Get("Authorization"), "Bearer ")
			if !ok {
				unauthorized(w)
				return
			}
			userID, err := i.Verify(raw)
			if err != nil {
				unauthorized(w)
				return
			}
			ctx := context.WithValue(r.Context(), ctxKey{}, userID)
			next.ServeHTTP(w, r.WithContext(ctx))
		}
		return http.HandlerFunc(fn)
	}
}

// UserID returns the authenticated user id set by RequireAuth.
func UserID(ctx context.Context) (int64, bool) {
	id, ok := ctx.Value(ctxKey{}).(int64)
	return id, ok
}

func unauthorized(w http.ResponseWriter) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusUnauthorized)
	w.Write([]byte(`{"error":"unauthorized"}`))
}
