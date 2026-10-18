import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from './AuthContext';
import type { UserRole } from '../../types';
import { ShieldAlert, Loader2 } from 'lucide-react';
import { Card, CardContent } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';

interface ProtectedRouteProps {
  children: React.ReactNode;
  allowedRoles?: UserRole[];
}

export function ProtectedRoute({ children, allowedRoles }: ProtectedRouteProps) {
  const { user, role, isLoading } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return (
      <div className="flex h-screen items-center justify-center bg-slate-50">
        <div className="flex flex-col items-center gap-3">
          <Loader2 className="w-8 h-8 animate-spin text-indigo-600" />
          <p className="text-sm font-medium text-slate-500">Authenticating session...</p>
        </div>
      </div>
    );
  }

  if (!user) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  if (allowedRoles && role && !allowedRoles.includes(role)) {
    return (
      <div className="flex min-h-[70vh] items-center justify-center p-6">
        <Card className="max-w-md w-full border-rose-200 bg-rose-50/30">
          <CardContent className="p-8 text-center space-y-4">
            <div className="w-12 h-12 rounded-full bg-rose-100 text-rose-600 flex items-center justify-center mx-auto">
              <ShieldAlert className="w-6 h-6" />
            </div>
            <div>
              <h2 className="text-lg font-bold text-slate-900">Access Restricted</h2>
              <p className="text-sm text-slate-600 mt-1">
                Your role <span className="font-semibold text-rose-700">[{role}]</span> does not have permission to view this resource.
              </p>
            </div>
            <p className="text-xs text-slate-500">
              Required role(s): {allowedRoles.join(', ')}
            </p>
            <Button variant="outline" onClick={() => window.history.back()} className="mt-2">
              Go Back
            </Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  return <>{children}</>;
}
