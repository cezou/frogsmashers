package httpapi

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/cezou/frogsmashers/api/internal/auth"
	"github.com/cezou/frogsmashers/api/internal/identity"
	"github.com/cezou/frogsmashers/api/internal/store"
)

type fakeProvider struct {
	id  string
	err error
}

func (p fakeProvider) VerifyCredential(
	ctx context.Context, credential string,
) (string, error) {
	return p.id, p.err
}

type fakeStore struct {
	users  map[string]store.User
	nextID int64
}

func newFakeStore() *fakeStore {
	return &fakeStore{users: map[string]store.User{}, nextID: 1000}
}

func (s *fakeStore) Create(
	ctx context.Context, provider, providerID string,
) (store.User, error) {
	key := provider + "|" + providerID
	if _, ok := s.users[key]; ok {
		return store.User{}, store.ErrExists
	}
	s.nextID++
	u := store.User{
		ID: s.nextID, Provider: provider, ProviderID: providerID,
		CreatedAt: time.Now(), LastLoginAt: time.Now(),
	}
	s.users[key] = u
	return u, nil
}

func (s *fakeStore) Login(
	ctx context.Context, provider, providerID string,
) (store.User, error) {
	u, ok := s.users[provider+"|"+providerID]
	if !ok {
		return store.User{}, store.ErrNotFound
	}
	u.LastLoginAt = time.Now()
	s.users[provider+"|"+providerID] = u
	return u, nil
}

type fakePinger struct{ err error }

func (p fakePinger) Ping(ctx context.Context) error { return p.err }

func newTestServer(provider identity.Provider, ping error) (
	http.Handler, *fakeStore, *auth.Issuer,
) {
	users := newFakeStore()
	issuer := auth.NewIssuer(
		[]byte("0123456789abcdef0123456789abcdef"), time.Hour)
	srv := NewServer(users, identity.Registry{"ugs": provider},
		issuer, fakePinger{err: ping}, slog.Default())
	return NewRouter(srv), users, issuer
}

func post(h http.Handler, path, body string) *httptest.ResponseRecorder {
	rec := httptest.NewRecorder()
	req := httptest.NewRequest(
		http.MethodPost, path, strings.NewReader(body))
	h.ServeHTTP(rec, req)
	return rec
}

const ugsBody = `{"provider":"ugs","token":"some-ugs-token"}`

func TestRegisterThenLogin(t *testing.T) {
	h, _, issuer := newTestServer(fakeProvider{id: "player-1"}, nil)

	rec := post(h, "/auth/register", ugsBody)
	if rec.Code != http.StatusCreated {
		t.Fatalf("register: code = %d, body %s", rec.Code, rec.Body)
	}
	var resp struct {
		UserID    string    `json:"user_id"`
		JWT       string    `json:"jwt"`
		ExpiresAt time.Time `json:"expires_at"`
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatalf("decode: %v", err)
	}
	if resp.UserID == "" || resp.JWT == "" || resp.ExpiresAt.IsZero() {
		t.Fatalf("incomplete response: %+v", resp)
	}
	uid, err := issuer.Verify(resp.JWT)
	if err != nil || fmt.Sprint(uid) != resp.UserID {
		t.Fatalf("jwt sub %d does not match user_id %s (%v)",
			uid, resp.UserID, err)
	}

	if rec := post(h, "/auth/register", ugsBody); rec.Code !=
		http.StatusConflict {
		t.Fatalf("second register: code = %d, want 409", rec.Code)
	}

	rec = post(h, "/auth/login", ugsBody)
	if rec.Code != http.StatusOK {
		t.Fatalf("login: code = %d", rec.Code)
	}
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatalf("decode login: %v", err)
	}

	rec = httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodGet, "/me", nil)
	req.Header.Set("Authorization", "Bearer "+resp.JWT)
	h.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK ||
		!strings.Contains(rec.Body.String(), resp.UserID) {
		t.Fatalf("/me: code = %d, body %s", rec.Code, rec.Body)
	}
}

func TestLoginUnregistered(t *testing.T) {
	h, _, _ := newTestServer(fakeProvider{id: "player-1"}, nil)
	if rec := post(h, "/auth/login", ugsBody); rec.Code !=
		http.StatusNotFound {
		t.Fatalf("code = %d, want 404", rec.Code)
	}
}

func TestAuthBadRequests(t *testing.T) {
	h, _, _ := newTestServer(fakeProvider{id: "player-1"}, nil)
	cases := map[string]string{
		"malformed json":   `{not json`,
		"missing token":    `{"provider":"ugs"}`,
		"unknown provider": `{"provider":"steam","token":"x"}`,
	}
	for name, body := range cases {
		if rec := post(h, "/auth/register", body); rec.Code !=
			http.StatusBadRequest {
			t.Errorf("%s: code = %d, want 400", name, rec.Code)
		}
	}
}

func TestAuthInvalidCredential(t *testing.T) {
	h, _, _ := newTestServer(
		fakeProvider{err: fmt.Errorf("%w: nope",
			identity.ErrInvalidCredential)}, nil)
	for _, path := range []string{"/auth/register", "/auth/login"} {
		if rec := post(h, path, ugsBody); rec.Code !=
			http.StatusUnauthorized {
			t.Errorf("%s: code = %d, want 401", path, rec.Code)
		}
	}
}

func TestAuthProviderInternalError(t *testing.T) {
	h, _, _ := newTestServer(
		fakeProvider{err: errors.New("jwks fetch broke")}, nil)
	if rec := post(h, "/auth/register", ugsBody); rec.Code !=
		http.StatusInternalServerError {
		t.Fatalf("code = %d, want 500", rec.Code)
	}
}

func TestMeWithoutToken(t *testing.T) {
	h, _, _ := newTestServer(fakeProvider{id: "p"}, nil)
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/me", nil))
	if rec.Code != http.StatusUnauthorized {
		t.Fatalf("code = %d, want 401", rec.Code)
	}
}

func TestHealthz(t *testing.T) {
	h, _, _ := newTestServer(fakeProvider{id: "p"}, nil)
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/healthz", nil))
	if rec.Code != http.StatusOK {
		t.Fatalf("healthy: code = %d", rec.Code)
	}

	h, _, _ = newTestServer(fakeProvider{id: "p"},
		errors.New("db down"))
	rec = httptest.NewRecorder()
	h.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/healthz", nil))
	if rec.Code != http.StatusServiceUnavailable {
		t.Fatalf("unhealthy: code = %d, want 503", rec.Code)
	}
}
