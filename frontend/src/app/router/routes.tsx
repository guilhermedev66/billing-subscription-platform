import { createBrowserRouter } from 'react-router-dom'
import { LoginPage } from '@/features/auth/LoginPage'
import { RegisterPage } from '@/features/auth/RegisterPage'
import { CustomersPage } from '@/pages/CustomersPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { DevelopersPage } from '@/pages/DevelopersPage'
import { InvoicesPage } from '@/pages/InvoicesPage'
import { NotFoundPage } from '@/pages/NotFoundPage'
import { PaymentsPage } from '@/pages/PaymentsPage'
import { ProductDetailPage } from '@/pages/ProductDetailPage'
import { ProductsPage } from '@/pages/ProductsPage'
import { SettingsPage } from '@/pages/SettingsPage'
import { SubscriptionsPage } from '@/pages/SubscriptionsPage'
import { AppLayout } from './AppLayout'
import { RedirectIfAuthenticated } from './RedirectIfAuthenticated'
import { RequireAuth } from './RequireAuth'

export const router = createBrowserRouter([
  {
    element: <RedirectIfAuthenticated />,
    children: [
      { path: '/login', element: <LoginPage /> },
      { path: '/register', element: <RegisterPage /> },
    ],
  },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { path: '/', element: <DashboardPage /> },
          { path: '/customers', element: <CustomersPage /> },
          { path: '/products', element: <ProductsPage /> },
          { path: '/products/:id', element: <ProductDetailPage /> },
          { path: '/subscriptions', element: <SubscriptionsPage /> },
          { path: '/invoices', element: <InvoicesPage /> },
          { path: '/payments', element: <PaymentsPage /> },
          { path: '/developers', element: <DevelopersPage /> },
          { path: '/settings', element: <SettingsPage /> },
        ],
      },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
])
