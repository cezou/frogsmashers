// Package identity abstracts the identity provider: a credential
// comes in, a verified provider-scoped player id comes out. Anonymous
// UGS tokens today, Steam auth tickets later — the users table and
// the JWT layer never change.
package identity

import (
	"context"
	"errors"
	"fmt"
)

// ErrInvalidCredential is returned when a provider rejects the
// presented credential (bad signature, expired, wrong audience...).
var ErrInvalidCredential = errors.New("invalid credential")

// Provider verifies a raw credential and returns the provider-scoped
// stable player id (UGS player id now, steamid64 as text later).
type Provider interface {
	VerifyCredential(ctx context.Context, credential string) (string, error)
}

// Registry maps a wire-level provider name ("ugs", "steam") to its
// implementation.
type Registry map[string]Provider

func (r Registry) Lookup(name string) (Provider, error) {
	p, ok := r[name]
	if !ok {
		return nil, fmt.Errorf("unknown identity provider %q", name)
	}
	return p, nil
}
