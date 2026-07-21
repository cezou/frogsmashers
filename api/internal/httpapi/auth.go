package httpapi

import (
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strconv"
	"time"

	"github.com/cezou/frogsmashers/api/internal/auth"
	"github.com/cezou/frogsmashers/api/internal/identity"
	"github.com/cezou/frogsmashers/api/internal/store"
)

const maxBodyBytes = 1 << 20

type authRequest struct {
	Provider string `json:"provider"`
	Token    string `json:"token"`
}

type authResponse struct {
	UserID    string    `json:"user_id"`
	JWT       string    `json:"jwt"`
	ExpiresAt time.Time `json:"expires_at"`
}

func (s *Server) handleRegister(w http.ResponseWriter, r *http.Request) {
	s.authenticate(w, r, true)
}

func (s *Server) handleLogin(w http.ResponseWriter, r *http.Request) {
	s.authenticate(w, r, false)
}

// authenticate implements both endpoints: the credential
// verification and JWT issuance are identical, only the user-row
// semantics differ (INSERT vs SELECT+touch).
func (s *Server) authenticate(
	w http.ResponseWriter, r *http.Request, register bool,
) {
	var req authRequest
	body := io.LimitReader(r.Body, maxBodyBytes)
	if err := json.NewDecoder(body).Decode(&req); err != nil ||
		req.Token == "" {
		writeError(w, http.StatusBadRequest, "bad_request")
		return
	}
	provider, err := s.providers.Lookup(req.Provider)
	if err != nil {
		writeError(w, http.StatusBadRequest, "unknown_provider")
		return
	}

	providerID, err := provider.VerifyCredential(r.Context(), req.Token)
	if errors.Is(err, identity.ErrInvalidCredential) {
		writeError(w, http.StatusUnauthorized, "invalid_credential")
		return
	}
	if err != nil {
		s.log.Error("credential verification failed",
			"provider", req.Provider, "err", err)
		writeError(w, http.StatusInternalServerError, "internal_error")
		return
	}

	var user store.User
	if register {
		user, err = s.users.Create(r.Context(), req.Provider, providerID)
		if errors.Is(err, store.ErrExists) {
			writeError(w, http.StatusConflict, "already_registered")
			return
		}
	} else {
		user, err = s.users.Login(r.Context(), req.Provider, providerID)
		if errors.Is(err, store.ErrNotFound) {
			writeError(w, http.StatusNotFound, "not_registered")
			return
		}
	}
	if err != nil {
		s.log.Error("user store failed", "err", err)
		writeError(w, http.StatusInternalServerError, "internal_error")
		return
	}

	token, expiresAt, err := s.issuer.Issue(user.ID, user.Provider)
	if err != nil {
		s.log.Error("jwt signing failed", "err", err)
		writeError(w, http.StatusInternalServerError, "internal_error")
		return
	}

	status := http.StatusOK
	if register {
		status = http.StatusCreated
	}
	writeJSON(w, status, authResponse{
		UserID:    strconv.FormatInt(user.ID, 10),
		JWT:       token,
		ExpiresAt: expiresAt.UTC(),
	})
}

// handleMe echoes the authenticated user id; it exists to exercise
// the Bearer middleware end-to-end until real endpoints arrive.
func (s *Server) handleMe(w http.ResponseWriter, r *http.Request) {
	id, ok := auth.UserID(r.Context())
	if !ok {
		writeError(w, http.StatusUnauthorized, "unauthorized")
		return
	}
	writeJSON(w, http.StatusOK,
		map[string]string{"user_id": strconv.FormatInt(id, 10)})
}
