import { NavLink } from 'react-router-dom';
import {
  LayoutDashboard,
  FileText,
  PackageCheck,
  Receipt,
  GitCompare,
  AlertTriangle,
  Network,
  History,
} from 'lucide-react';
import { cn } from '../../lib/utils';

const navItems = [
  { to: '/dashboard', label: 'Operations Overview', icon: LayoutDashboard },
  { to: '/purchase-orders', label: 'Purchase Orders', icon: FileText },
  { to: '/goods-receipts', label: 'Goods Receipts', icon: PackageCheck },
  { to: '/invoices', label: 'Supplier Invoices', icon: Receipt },
  { to: '/reconciliation', label: '3-Way Matching Workspace', icon: GitCompare },
  { to: '/discrepancies', label: 'Discrepancy Review', icon: AlertTriangle },
  { to: '/integrations', label: 'Integration Operations', icon: Network },
  { to: '/audit', label: 'Audit Timeline', icon: History },
];

export function Sidebar() {
  return (
    <aside className="w-64 bg-slate-900 border-r border-slate-800 flex flex-col shrink-0 min-h-[calc(100vh-4rem)]">
      <div className="p-4 space-y-1">
        <div className="text-[11px] font-bold text-slate-500 uppercase tracking-wider px-3 mb-2">
          Operations Core
        </div>
        {navItems.map((item) => {
          const Icon = item.icon;
          return (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors',
                  isActive
                    ? 'bg-indigo-600 text-white shadow-xs'
                    : 'text-slate-400 hover:text-white hover:bg-slate-800/60'
                )
              }
            >
              <Icon className="w-4 h-4 shrink-0" />
              <span>{item.label}</span>
            </NavLink>
          );
        })}
      </div>

      <div className="mt-auto p-4 border-t border-slate-800">
        <div className="bg-slate-800/60 rounded-xl p-3 border border-slate-700/50">
          <div className="text-xs font-semibold text-slate-300">Phase G4 Operations</div>
          <div className="text-[11px] text-slate-400 mt-1">
            Reconciliation Engine v1.0
          </div>
          <div className="flex items-center gap-1.5 mt-2">
            <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
            <span className="text-[10px] text-emerald-400 font-medium">PostgreSQL & Bus Live</span>
          </div>
        </div>
      </div>
    </aside>
  );
}
