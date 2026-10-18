import React from 'react';
import { cn } from '../../lib/utils';

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: 'default' | 'success' | 'warning' | 'destructive' | 'info' | 'purple' | 'outline';
}

export function Badge({ className, variant = 'default', children, ...props }: BadgeProps) {
  const baseStyles = 'inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold uppercase tracking-wider';

  const variants = {
    default: 'bg-slate-100 text-slate-800',
    success: 'bg-emerald-100 text-emerald-800 border border-emerald-200',
    warning: 'bg-amber-100 text-amber-800 border border-amber-200',
    destructive: 'bg-rose-100 text-rose-800 border border-rose-200',
    info: 'bg-blue-100 text-blue-800 border border-blue-200',
    purple: 'bg-purple-100 text-purple-800 border border-purple-200',
    outline: 'border border-slate-300 text-slate-700 bg-white',
  };

  return (
    <span className={cn(baseStyles, variants[variant], className)} {...props}>
      {children}
    </span>
  );
}

export function StatusBadge({ status }: { status: string }) {
  const norm = status?.toLowerCase() || '';

  if (norm === 'matched' || norm === 'completed' || norm === 'resolved') {
    return <Badge variant="success">{status}</Badge>;
  }
  if (norm === 'approvedwithvariance' || norm === 'approved') {
    return <Badge variant="purple">{status === 'ApprovedWithVariance' ? 'Approved (Variance)' : status}</Badge>;
  }
  if (norm === 'discrepancy' || norm === 'underreview' || norm === 'partiallymatched') {
    return <Badge variant="warning">{status}</Badge>;
  }
  if (norm === 'rejected' || norm === 'failed' || norm === 'cancelled' || norm === 'error') {
    return <Badge variant="destructive">{status}</Badge>;
  }
  if (norm === 'pendingreconciliation' || norm === 'pending' || norm === 'draft' || norm === 'open') {
    return <Badge variant="info">{status}</Badge>;
  }

  return <Badge variant="default">{status}</Badge>;
}

export function DiscrepancySeverityBadge({ severity }: { severity: string }) {
  if (severity === 'Error') {
    return <Badge variant="destructive">Error</Badge>;
  }
  return <Badge variant="warning">Warning</Badge>;
}
