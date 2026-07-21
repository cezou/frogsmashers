// Package httpapi assembles the HTTP surface of the accounts
// backend: /auth (register, login), /healthz, and the authenticated
// group future endpoints will join.
package httpapi

import (
	"context"
	"encoding/json"
	"log/slog"
	"net/http"
	"time"

	"github.com/go-chi/chi/v5"
	"github.com/go-chi/chi/v5/middleware"

	"github.com/cezou/frogsmashers/api/internal/auth"
	"github.com/cezou/frogsmashers/api/internal/identity"
	"github.com/cezou/frogsmashers/api/internal/store"
)

// Pinger reports database liveness for /healthz; *pgxpool.Pool
// satisfies it.
type Pinger interface {
	Ping(ctx context.Context) error
}

type Server struct {
	users     store.UserStore
	providers identity.Registry
	issuer    *auth.Issuer
	pinger    Pinger
	log       *slog.Logger
}

func NewServer(
	users store.UserStore,
	providers identity.Registry,
	issuer *auth.Issuer,
	pinger Pinger,
	log *slog.Logger,
) *Server {
	return &Server{
		users:     users,
		providers: providers,
		issuer:    issuer,
		pinger:    pinger,
		log:       log,
	}
}

func NewRouter(s *Server) http.Handler {
	r := chi.NewRouter()
	r.Use(middleware.RequestID)
	r.Use(middleware.RealIP)
	r.Use(middleware.Recoverer)
	r.Use(middleware.Timeout(15 * time.Second))

	r.Get("/healthz", s.handleHealthz)
	r.Route("/auth", func(r chi.Router) {
		r.Post("/register", s.handleRegister)
		r.Post("/login", s.handleLogin)
	})
	r.Group(func(r chi.Router) {
		r.Use(auth.RequireAuth(s.issuer))
		r.Get("/me", s.handleMe)
	})
	return r
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(body)
}

func writeError(w http.ResponseWriter, status int, code string) {
	writeJSON(w, status, map[string]string{"error": code})
}
