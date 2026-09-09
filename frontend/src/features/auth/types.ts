export interface AuthUser {
  id: string
  email: string
  organizationId: string
  organizationName: string
}

export interface AuthSession {
  token: string
  user: AuthUser
}
