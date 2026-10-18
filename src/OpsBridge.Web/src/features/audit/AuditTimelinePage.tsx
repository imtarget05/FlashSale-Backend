import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../../lib/api-client';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Select } from '../../components/ui/Select';
import { formatDate } from '../../lib/formatters';
import {
  History,
  Search,
  Filter,
  Shield,
  User,
  Clock,
  RefreshCw,
} from 'lucide-react';

export function AuditTimelinePage() {
  const [filterType, setFilterType] = useState<string>('ALL');
  const [searchTerm, setSearchTerm] = useState('');

  const { data: events, isLoading, error, refetch } = useQuery({
    queryKey: ['audit-trail'],
    queryFn: () => api.getAuditTrail(),
    refetchInterval: 15000,
  });

  const filteredEvents = (events || []).filter((e) => {
    const matchesType = filterType === 'ALL' || e.entityType.toLowerCase() === filterType.toLowerCase();
    const matchesSearch =
      e.action.toLowerCase().includes(searchTerm.toLowerCase()) ||
      e.userId.toLowerCase().includes(searchTerm.toLowerCase()) ||
      e.details.toLowerCase().includes(searchTerm.toLowerCase()) ||
      e.entityId.toLowerCase().includes(searchTerm.toLowerCase());
    return matchesType && matchesSearch;
  });

  const getActionBadgeColor = (action: string) => {
    if (action.includes('Approved') || action.includes('Matched')) {
      return 'bg-emerald-100 text-emerald-800 border-emerald-200';
    }
    if (action.includes('Reject') || action.includes('Failed')) {
      return 'bg-rose-100 text-rose-800 border-rose-200';
    }
    if (action.includes('Discrepancy') || action.includes('Variance')) {
      return 'bg-amber-100 text-amber-800 border-amber-200';
    }
    return 'bg-blue-100 text-blue-800 border-blue-200';
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900 flex items-center gap-2">
            <History className="w-6 h-6 text-blue-600" />
            <span>Audit & Compliance Timeline</span>
          </h1>
          <p className="text-sm text-slate-500 mt-1">
            Immutable enterprise audit log for SOX compliance, reconciliation decisions, and operator interventions.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => refetch()}
            className="flex items-center gap-1.5"
          >
            <RefreshCw className="w-4 h-4 text-slate-500" />
            <span>Refresh</span>
          </Button>
        </div>
      </div>

      {/* Filter Bar */}
      <Card>
        <CardContent className="p-4">
          <div className="flex flex-col sm:flex-row items-center gap-3">
            <div className="relative flex-1 w-full">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                type="text"
                placeholder="Search audit trail by user, action, entity ID or details..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="w-full pl-9 pr-4 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div className="flex items-center gap-2 w-full sm:w-auto">
              <Filter className="w-4 h-4 text-slate-400" />
              <Select
                value={filterType}
                onChange={(e) => setFilterType(e.target.value)}
                className="text-sm min-w-[170px]"
              >
                <option value="ALL">All Entity Types</option>
                <option value="SupplierInvoice">Supplier Invoices</option>
                <option value="Discrepancy">Discrepancies</option>
                <option value="GoodsReceipt">Goods Receipts</option>
                <option value="PurchaseOrder">Purchase Orders</option>
              </Select>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Timeline Stream */}
      <Card>
        <CardHeader className="border-b border-slate-100 pb-3">
          <CardTitle className="text-base font-semibold text-slate-900 flex items-center gap-2">
            <Shield className="w-4 h-4 text-slate-600" />
            <span>Audit Records ({filteredEvents.length})</span>
          </CardTitle>
        </CardHeader>
        <CardContent className="p-6">
          {isLoading ? (
            <div className="p-8 text-center text-sm text-slate-500">Loading audit records...</div>
          ) : error ? (
            <div className="p-8 text-center text-sm text-rose-600">Failed to load audit trail.</div>
          ) : filteredEvents.length === 0 ? (
            <div className="p-10 text-center">
              <History className="w-8 h-8 text-slate-300 mx-auto mb-2" />
              <p className="text-sm font-semibold text-slate-800">No audit records found</p>
              <p className="text-xs text-slate-500 mt-0.5">
                Audit logs are recorded automatically as actions are taken in OpsBridge.
              </p>
            </div>
          ) : (
            <div className="relative pl-6 space-y-6 before:absolute before:left-2 before:top-2 before:bottom-2 before:w-0.5 before:bg-slate-200">
              {filteredEvents.map((evt) => (
                <div key={evt.id} className="relative group">
                  {/* Timeline Node Icon */}
                  <div className="absolute -left-[31px] top-1 w-5 h-5 rounded-full bg-white border-2 border-blue-500 flex items-center justify-center group-hover:scale-110 transition-transform">
                    <div className="w-2 h-2 rounded-full bg-blue-600" />
                  </div>

                  <div className="p-4 bg-slate-50/75 hover:bg-slate-50 border border-slate-200/80 rounded-lg transition-colors">
                    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 mb-2">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className={`text-[11px] font-semibold px-2 py-0.5 rounded-full border ${getActionBadgeColor(evt.action)}`}>
                          {evt.action}
                        </span>
                        <span className="text-xs font-mono font-medium text-slate-600 bg-slate-200/70 px-2 py-0.5 rounded">
                          {evt.entityType}
                        </span>
                        <span className="text-xs font-mono text-slate-400 truncate max-w-[200px]">
                          ID: {evt.entityId}
                        </span>
                      </div>
                      <div className="flex items-center gap-3 text-xs text-slate-500">
                        <div className="flex items-center gap-1">
                          <User className="w-3.5 h-3.5 text-slate-400" />
                          <span className="font-semibold text-slate-700">{evt.userId}</span>
                        </div>
                        <div className="flex items-center gap-1">
                          <Clock className="w-3.5 h-3.5 text-slate-400" />
                          <span>{formatDate(evt.timestamp)}</span>
                        </div>
                      </div>
                    </div>

                    <div className="text-xs text-slate-700 bg-white p-3 rounded border border-slate-200/60 font-mono whitespace-pre-wrap break-words">
                      {evt.details}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
