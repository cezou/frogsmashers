package identity

import (
	"context"
	"testing"
)

type staticProvider struct{ id string }

func (p staticProvider) VerifyCredential(
	ctx context.Context, credential string,
) (string, error) {
	return p.id, nil
}

func TestRegistryLookup(t *testing.T) {
	reg := Registry{"ugs": staticProvider{id: "player-1"}}

	p, err := reg.Lookup("ugs")
	if err != nil {
		t.Fatalf("Lookup(ugs): %v", err)
	}
	id, err := p.VerifyCredential(context.Background(), "any")
	if err != nil || id != "player-1" {
		t.Fatalf("VerifyCredential = %q, %v", id, err)
	}

	if _, err := reg.Lookup("steam"); err == nil {
		t.Fatal("Lookup(steam) should fail: not registered")
	}
}
