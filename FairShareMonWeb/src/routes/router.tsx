import { createBrowserRouter, Navigate } from "react-router-dom";
import { RootLayout } from "./RootLayout";
import { PublicOnlyRoute } from "./PublicOnlyRoute";
import { ProtectedRoute } from "./ProtectedRoute";
import { AdminRoute } from "./AdminRoute";
import { AppShellLayout } from "./AppShellLayout";
import { NotFound } from "./NotFound";
import { StubPage } from "./StubPage";
import { LoginPage } from "@/features/auth/pages/LoginPage";
import { RegisterPage } from "@/features/auth/pages/RegisterPage";
import { ChangePasswordPage } from "@/features/auth/pages/ChangePasswordPage";
import { DashboardPage } from "@/features/dashboard/pages/DashboardPage";
import { SettingsPage } from "@/features/settings/pages/SettingsPage";
import { MembersPage } from "@/features/members/pages/MembersPage";
import { CategoriesPage } from "@/features/categories/pages/CategoriesPage";
import { TagsPage } from "@/features/tags/pages/TagsPage";
import { AdminPage } from "@/features/admin/pages/AdminPage";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <RootLayout />,
    children: [
      // Public auth routes (redirect to app if already signed in).
      {
        element: <PublicOnlyRoute />,
        children: [
          { path: "login", element: <LoginPage /> },
          { path: "register", element: <RegisterPage /> },
        ],
      },
      // Authenticated app shell.
      {
        element: <ProtectedRoute />,
        children: [
          {
            element: <AppShellLayout />,
            children: [
              { index: true, element: <Navigate to="/dashboard" replace /> },
              { path: "dashboard", element: <DashboardPage /> },
              { path: "members", element: <MembersPage /> },
              { path: "categories", element: <CategoriesPage /> },
              { path: "tags", element: <TagsPage /> },
              {
                path: "expenses",
                element: <StubPage titleKey="common:nav.expenses" />,
              },
              {
                path: "events",
                element: <StubPage titleKey="common:nav.events" />,
              },
              {
                path: "stats",
                element: <StubPage titleKey="common:nav.stats" />,
              },
              {
                path: "wallet",
                element: <StubPage titleKey="common:nav.wallet" />,
              },
              {
                path: "settings",
                children: [
                  { index: true, element: <SettingsPage /> },
                  {
                    path: "change-password",
                    element: <ChangePasswordPage />,
                  },
                ],
              },
              // Admin area — gated on role == ADMIN (from /auth/me; see AdminRoute).
              {
                path: "admin",
                element: <AdminRoute />,
                children: [{ index: true, element: <AdminPage /> }],
              },
            ],
          },
        ],
      },
      // Ownership 404s + unknown paths.
      { path: "*", element: <NotFound /> },
    ],
  },
]);
