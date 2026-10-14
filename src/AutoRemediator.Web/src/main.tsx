import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, RouterProvider } from 'react-router'
import '@fontsource-variable/inter'
import './index.css'
import { AppShell } from '@/components/app-shell'
import { HomePage } from '@/pages/home'
import { RunsPage } from '@/pages/runs'
import { RunDetailPage } from '@/pages/run-detail'
import { DependencyMapPage } from '@/pages/dependency-map'
import { ConfigurationPage } from '@/pages/configuration'
import { RepositoryPage } from '@/pages/repository'
import { NotFoundPage } from '@/pages/not-found'

const router = createBrowserRouter([
  {
    element: <AppShell />,
    children: [
      { path: '/', element: <HomePage /> },
      { path: '/runs', element: <RunsPage /> },
      { path: '/runs/:id', element: <RunDetailPage /> },
      { path: '/dependency-map', element: <DependencyMapPage /> },
      { path: '/configuration', element: <ConfigurationPage /> },
      { path: '/configuration/repositories/:id', element: <RepositoryPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
)
