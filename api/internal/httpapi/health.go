package httpapi

import (
	"context"
	"net/http"
	"time"
)

func (s *Server) handleHealthz(w http.ResponseWriter, r *http.Request) {
	ctx, cancel := context.WithTimeout(r.Context(), 2*time.Second)
	defer cancel()
	if err := s.pinger.Ping(ctx); err != nil {
		writeJSON(w, http.StatusServiceUnavailable,
			map[string]string{"status": "db_unavailable"})
		return
	}
	writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
}
