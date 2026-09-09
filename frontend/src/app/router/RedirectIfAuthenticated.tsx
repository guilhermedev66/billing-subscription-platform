import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '@/features/auth/AuthContext'

export function RedirectIfAuthenticated() {
  const { user } = useAuth()

  if (user) return <Navigate to="/" replace />

  return <Outlet />
}
