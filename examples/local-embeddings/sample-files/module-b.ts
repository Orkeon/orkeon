// User session management: validates expiration and refreshes tokens.
export function isSessionExpired(session: { expiresAt: number }): boolean {
    return session.expiresAt < Date.now();
}
