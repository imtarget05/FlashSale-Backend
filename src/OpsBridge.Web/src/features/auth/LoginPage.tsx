import React, { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from './AuthContext';
import type { UserRole } from '../../types';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Layers, ShieldCheck, UserCheck, AlertCircle } from 'lucide-react';

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = location.state?.from?.pathname || '/dashboard';

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim()) {
      setError('Username is required.');
      return;
    }
    setError(null);
    setLoading(true);

    try {
      await login(username);
      navigate(from, { replace: true });
    } catch (err: any) {
      setError(err.message || 'Authentication failed. Please verify credentials.');
    } finally {
      setLoading(false);
    }
  };

  const handleQuickPersona = async (personaUsername: string, personaRole: UserRole) => {
    setError(null);
    setLoading(true);
    try {
      await login(personaUsername, personaRole);
      navigate(from, { replace: true });
    } catch (err: any) {
      setError(err.message || 'Failed to authenticate persona.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-900 flex flex-col justify-center items-center p-4">
      <div className="w-full max-w-md space-y-6">
        {/* Brand Header */}
        <div className="text-center space-y-2">
          <div className="inline-flex p-3 rounded-2xl bg-indigo-600/20 text-indigo-400 border border-indigo-500/30">
            <Layers className="w-8 h-8" />
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-white">OpsBridge Platform</h1>
          <p className="text-sm text-slate-400">
            Enterprise Integration, 3-Way Reconciliation & Operations
          </p>
        </div>

        {/* Login Card */}
        <Card className="border-slate-800 bg-slate-800/80 backdrop-blur-sm shadow-2xl">
          <CardHeader className="border-slate-700/60 pb-4">
            <CardTitle className="text-white text-base">Sign In to Operations Console</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4 pt-4">
            {error && (
              <div className="p-3 rounded-lg bg-rose-500/10 border border-rose-500/30 text-rose-400 text-xs flex items-center gap-2">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            <form onSubmit={handleSubmit} className="space-y-3.5">
              <div>
                <label className="block text-xs font-semibold text-slate-300 mb-1">Username / Email</label>
                <Input
                  type="text"
                  placeholder="e.g. finance_reviewer"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  className="bg-slate-900/60 border-slate-700 text-white placeholder:text-slate-500"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-300 mb-1">Password</label>
                <Input
                  type="password"
                  placeholder="••••••••"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="bg-slate-900/60 border-slate-700 text-white placeholder:text-slate-500"
                />
              </div>

              <Button type="submit" variant="primary" className="w-full" isLoading={loading}>
                Sign In
              </Button>
            </form>

            <div className="relative my-4">
              <div className="absolute inset-0 flex items-center">
                <div className="w-full border-t border-slate-700"></div>
              </div>
              <div className="relative flex justify-center text-xs uppercase">
                <span className="bg-slate-800 px-2 text-slate-400 font-medium">Or Quick Switch Persona</span>
              </div>
            </div>

            {/* Quick Persona Buttons */}
            <div className="grid grid-cols-2 gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => handleQuickPersona('alice_finance', 'FinanceReviewer')}
                className="bg-slate-900/40 border-slate-700 text-slate-300 hover:text-white hover:bg-slate-700/50 justify-start text-xs"
              >
                <ShieldCheck className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                <span>Finance Reviewer</span>
              </Button>

              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => handleQuickPersona('bob_operator', 'Operator')}
                className="bg-slate-900/40 border-slate-700 text-slate-300 hover:text-white hover:bg-slate-700/50 justify-start text-xs"
              >
                <UserCheck className="w-3.5 h-3.5 text-blue-400 shrink-0" />
                <span>Operator</span>
              </Button>

              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => handleQuickPersona('admin_system', 'Admin')}
                className="bg-slate-900/40 border-slate-700 text-slate-300 hover:text-white hover:bg-slate-700/50 justify-start text-xs"
              >
                <UserCheck className="w-3.5 h-3.5 text-purple-400 shrink-0" />
                <span>Administrator</span>
              </Button>

              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => handleQuickPersona('compliance_auditor', 'Auditor')}
                className="bg-slate-900/40 border-slate-700 text-slate-300 hover:text-white hover:bg-slate-700/50 justify-start text-xs"
              >
                <ShieldCheck className="w-3.5 h-3.5 text-amber-400 shrink-0" />
                <span>Auditor</span>
              </Button>
            </div>
          </CardContent>
        </Card>

        <p className="text-center text-xs text-slate-500">
          OpsBridge Reliability Platform &copy; 2026. Microservices & 3-Way Matching Engine.
        </p>
      </div>
    </div>
  );
}
