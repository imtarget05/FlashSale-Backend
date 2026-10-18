import { useQuery } from '@tanstack/react-query';
import { api } from '../../lib/api-client';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { formatDate } from '../../lib/formatters';
import {
  Layers,
  CheckCircle2,
  AlertTriangle,
  Skull,
  ArrowRight,
  TrendingUp,
  History,
  RefreshCw,
} from 'lucide-react';
import { Link } from 'react-router-dom';

export function DashboardPage() {
  const {
    data: metrics,
    isLoading: loadingMetrics,
    refetch: refetchMetrics,
  } = useQuery({
    queryKey: ['dashboard-metrics'],
    queryFn: () => api.getDashboardMetrics(),
    refetchInterval: 10000,
  });

  const { data: auditEvents, isLoading: loadingAudit } = useQuery({
    queryKey: ['recent-audit'],
    queryFn: () => api.getAuditTrail(),
  });

  return (
    <div className="space-y-6">
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Operations Overview</h1>
          <p className="text-sm text-slate-500 mt-1">
            Real-time reconciliation status, accounts payable matching, and integration telemetry.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetchMetrics()}
            className="flex items-center gap-1.5"
          >
            <RefreshCw className="w-4 h-4 text-slate-500" />
            <span>Refresh</span>
          </Button>
          <Link to="/reconciliation">
            <Button variant="primary" size="sm" className="flex items-center gap-1.5">
              <span>Matching Workspace</span>
              <ArrowRight className="w-4 h-4" />
            </Button>
          </Link>
        </div>
      </div>

      {/* KPI Metric Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Total Invoices */}
        <Card className="border-l-4 border-l-indigo-500">
          <CardContent className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider">
                Total Invoices
              </span>
              <div className="p-2 rounded-lg bg-indigo-50 text-indigo-600">
                <Layers className="w-4 h-4" />
              </div>
            </div>
            <div className="mt-3">
              <div className="text-2xl font-bold text-slate-900">
                {loadingMetrics ? '...' : metrics?.totalInvoices ?? 0}
              </div>
              <div className="text-xs text-slate-500 mt-1 flex items-center gap-1">
                <span>{metrics?.totalBatches ?? 0} reconciliation batches executed</span>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Matched Invoices */}
        <Card className="border-l-4 border-l-emerald-500">
          <CardContent className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider">
                Matched & Approved
              </span>
              <div className="p-2 rounded-lg bg-emerald-50 text-emerald-600">
                <CheckCircle2 className="w-4 h-4" />
              </div>
            </div>
            <div className="mt-3">
              <div className="text-2xl font-bold text-emerald-700">
                {loadingMetrics ? '...' : metrics?.matchedInvoices ?? 0}
              </div>
              <div className="text-xs text-slate-500 mt-1 flex items-center gap-1">
                <TrendingUp className="w-3.5 h-3.5 text-emerald-600" />
                <span>Zero variance or approved tolerance</span>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Open Discrepancies */}
        <Card className="border-l-4 border-l-amber-500">
          <CardContent className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider">
                Open Discrepancies
              </span>
              <div className="p-2 rounded-lg bg-amber-50 text-amber-600">
                <AlertTriangle className="w-4 h-4" />
              </div>
            </div>
            <div className="mt-3">
              <div className="text-2xl font-bold text-amber-700">
                {loadingMetrics ? '...' : metrics?.openDiscrepancies ?? 0}
              </div>
              <div className="text-xs text-slate-500 mt-1 flex items-center gap-1">
                <span>{metrics?.discrepantInvoices ?? 0} invoices requiring investigation</span>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Integration DLQ */}
        <Card className="border-l-4 border-l-rose-500">
          <CardContent className="p-5">
            <div className="flex items-center justify-between">
              <span className="text-xs font-semibold text-slate-500 uppercase tracking-wider">
                Dead-Letter Queue
              </span>
              <div className="p-2 rounded-lg bg-rose-50 text-rose-600">
                <Skull className="w-4 h-4" />
              </div>
            </div>
            <div className="mt-3">
              <div className="text-2xl font-bold text-rose-700">
                {loadingMetrics ? '...' : metrics?.deadLetterCount ?? 0}
              </div>
              <div className="text-xs text-slate-500 mt-1 flex items-center gap-1">
                <span>{metrics?.activeConnectors ?? 1} active connectors online</span>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Operational Highlights & Activity Feed */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left Column: Quick Action Cards */}
        <div className="space-y-4 lg:col-span-1">
          <Card className="bg-gradient-to-br from-indigo-900 to-slate-900 text-white border-0 shadow-lg">
            <CardContent className="p-6 space-y-4">
              <div className="w-10 h-10 rounded-xl bg-white/10 flex items-center justify-center text-white">
                <CheckCircle2 className="w-6 h-6 text-emerald-400" />
              </div>
              <div>
                <h3 className="text-base font-bold">Accounts Payable 3-Way Engine</h3>
                <p className="text-xs text-slate-300 mt-1 leading-relaxed">
                  Deterministic line-level matching between Purchase Orders, Goods Receipts, and Supplier Invoices with multi-shipment cumulative allocation.
                </p>
              </div>
              <div className="pt-2">
                <Link to="/reconciliation">
                  <Button variant="primary" size="sm" className="bg-indigo-500 hover:bg-indigo-600 text-white w-full">
                    Launch Matching Workspace
                  </Button>
                </Link>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="py-3 px-4">
              <CardTitle className="text-xs uppercase tracking-wider text-slate-500">
                System Topology Health
              </CardTitle>
            </CardHeader>
            <CardContent className="p-4 space-y-3">
              <div className="flex items-center justify-between text-xs">
                <span className="font-medium text-slate-700 flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full bg-emerald-500"></span>
                  FactoryFlow REST & Webhooks
                </span>
                <span className="text-emerald-700 font-semibold">Active</span>
              </div>
              <div className="flex items-center justify-between text-xs">
                <span className="font-medium text-slate-700 flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full bg-emerald-500"></span>
                  PostgreSQL 15 Persistence
                </span>
                <span className="text-emerald-700 font-semibold">Healthy</span>
              </div>
              <div className="flex items-center justify-between text-xs">
                <span className="font-medium text-slate-700 flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full bg-emerald-500"></span>
                  RabbitMQ 3 Queue Bus
                </span>
                <span className="text-emerald-700 font-semibold">Connected</span>
              </div>
              <div className="flex items-center justify-between text-xs">
                <span className="font-medium text-slate-700 flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full bg-blue-500"></span>
                  Idempotent Consumer Host
                </span>
                <span className="text-blue-700 font-semibold">Listening</span>
              </div>
            </CardContent>
          </Card>
        </div>

        {/* Right Column: Live Audit Activity */}
        <div className="lg:col-span-2">
          <Card className="h-full">
            <CardHeader className="py-4 px-5">
              <div className="flex items-center justify-between w-full">
                <div className="flex items-center gap-2">
                  <History className="w-4 h-4 text-indigo-600" />
                  <CardTitle className="text-sm font-semibold">Live Audit & Activity Trail</CardTitle>
                </div>
                <Link to="/audit" className="text-xs font-medium text-indigo-600 hover:text-indigo-800">
                  View All &rarr;
                </Link>
              </div>
            </CardHeader>
            <CardContent className="p-0">
              {loadingAudit ? (
                <div className="p-8 text-center text-xs text-slate-500">Loading audit events...</div>
              ) : !auditEvents || auditEvents.length === 0 ? (
                <div className="p-8 text-center text-xs text-slate-400">
                  No activity events recorded yet.
                </div>
              ) : (
                <div className="divide-y divide-slate-100 max-h-[380px] overflow-y-auto">
                  {auditEvents.slice(0, 7).map((ev) => (
                    <div key={ev.id} className="p-4 hover:bg-slate-50/80 transition-colors flex items-start gap-3">
                      <div className="w-2 h-2 rounded-full bg-indigo-500 mt-2 shrink-0"></div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-xs font-semibold text-slate-900 truncate">
                            {ev.action}
                          </span>
                          <span className="text-[10px] text-slate-400 shrink-0">
                            {formatDate(ev.timestamp)}
                          </span>
                        </div>
                        <p className="text-xs text-slate-600 mt-0.5 truncate">
                          {ev.details}
                        </p>
                        <div className="flex items-center gap-2 mt-1">
                          <span className="text-[10px] bg-slate-100 text-slate-600 px-1.5 py-0.5 rounded font-mono">
                            {ev.entityType}
                          </span>
                          <span className="text-[10px] text-slate-500">
                            by <span className="font-medium text-slate-700">{ev.userId}</span>
                          </span>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
