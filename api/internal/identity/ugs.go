package identity

import (
	"context"
	"fmt"

	"github.com/MicahParks/keyfunc/v3"
	"github.com/golang-jwt/jwt/v5"
)

// UGSProvider verifies Unity Gaming Services player-auth access
// tokens (RS256 JWTs) against Unity's JWKS. The client obtains one
// via anonymous sign-in and sends it as the credential.
type UGSProvider struct {
	keyfunc   jwt.Keyfunc
	issuer    string
	projectID string
}

// NewUGSProvider fetches the JWKS once and keeps it refreshed in the
// background (keyfunc default behaviour, including refresh on
// unknown kid).
func NewUGSProvider(
	ctx context.Context, jwksURL, issuer, projectID string,
) (*UGSProvider, error) {
	kf, err := keyfunc.NewDefaultCtx(ctx, []string{jwksURL})
	if err != nil {
		return nil, fmt.Errorf("load UGS JWKS %s: %w", jwksURL, err)
	}
	return &UGSProvider{
		keyfunc:   kf.Keyfunc,
		issuer:    issuer,
		projectID: projectID,
	}, nil
}

func (p *UGSProvider) VerifyCredential(
	ctx context.Context, credential string,
) (string, error) {
	claims := jwt.MapClaims{}
	_, err := jwt.ParseWithClaims(credential, claims, p.keyfunc,
		jwt.WithValidMethods([]string{"RS256"}),
		jwt.WithIssuer(p.issuer),
		jwt.WithExpirationRequired(),
	)
	if err != nil {
		return "", fmt.Errorf("%w: %w", ErrInvalidCredential, err)
	}
	if !p.audienceMatches(claims) {
		return "", fmt.Errorf("%w: audience mismatch", ErrInvalidCredential)
	}
	sub, err := claims.GetSubject()
	if err != nil || sub == "" {
		return "", fmt.Errorf("%w: missing subject", ErrInvalidCredential)
	}
	return sub, nil
}

// audienceMatches accepts either the bare project id or the
// "upid:<projectId>" form observed in UGS tokens.
func (p *UGSProvider) audienceMatches(claims jwt.MapClaims) bool {
	aud, err := claims.GetAudience()
	if err != nil {
		return false
	}
	for _, a := range aud {
		if a == p.projectID || a == "upid:"+p.projectID {
			return true
		}
	}
	return false
}
