import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { api } from '../../lib/api-client';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Select } from '../../components/ui/Select';
import { Modal } from '../../components/ui/Modal';
import { StatusBadge } from '../../components/ui/Badge';
import { formatCurrency, formatQuantity, formatDate } from '../../lib/formatters';
import type { SupplierInvoice } from '../../types';
import {
  Receipt,
  Plus,
  Search,
  Eye,
  Upload,
  Play,
  CheckCircle2,
  AlertCircle,
  Clock,
  Filter,
} from 'lucide-react';

export function InvoicesPage() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('ALL');
  const [selectedInvoice, setSelectedInvoice] = useState<SupplierInvoice | null>(null);
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [isCsvOpen, setIsCsvOpen] = useState(false);

  // Create Form State
  const [invoiceNumber, setInvoiceNumber] = useState('INV-2026-901');
  const [purchaseOrderNumber, setPurchaseOrderNumber] = useState('PO-2026-1001');
  const [supplierCode, setSupplierCode] = useState('SUP-ACME-01');
  const [supplierName, setSupplierName] = useState('Acme Industrial Supply');
  const [currency, setCurrency] = useState('VND');
  const [sku, setSku] = useState('RM-008');
  const [description, setDescription] = useState('Raw Steel Rods 10mm');
  const [quantity, setQuantity] = useState('100');
  const [unitPrice, setUnitPrice] = useState('20000');
  const [taxRate, setTaxRate] = useState('0.10');

  const [csvContent, setCsvContent] = useState(
`InvoiceNumber,PurchaseOrderNumber,SupplierCode,SupplierName,Currency,InvoiceDate,LineNumber,ItemSku,Description,InvoicedQuantity,UnitOfMeasure,UnitPrice,TaxRate
INV-2026-901,PO-2026-1001,SUP-ACME-01,Acme Industrial Supply,VND,2026-10-18T10:00:00Z,1,RM-008,Raw Steel Rods 10mm,100,PCS,20000,0.1`
  );

  const { data: invoices, isLoading, error } = useQuery({
    queryKey: ['invoices'],
    queryFn: () => api.getInvoices(),
  });

  const createMutation = useMutation({
    mutationFn: (data: any) => api.createInvoice(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['invoices'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard-metrics'] });
      setIsCreateOpen(false);
    },
  });

  const csvMutation = useMutation({
    mutationFn: (csv: string) => api.importInvoicesCsv(csv),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['invoices'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard-metrics'] });
      setIsCsvOpen(false);
    },
  });

  const handleCreateSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    createMutation.mutate({
      invoiceNumber,
      purchaseOrderNumber,
      supplierCode,
      supplierName,
      currency,
      lines: [
        {
          lineNumber: 1,
          itemSku: sku,
          description,
          invoicedQuantity: parseFloat(quantity) || 0,
          unitOfMeasure: 'PCS',
          unitPrice: parseFloat(unitPrice) || 0,
          taxRate: parseFloat(taxRate) || 0,
        },
      ],
    });
  };

  const filteredInvoices = (invoices || []).filter((inv) => {
    const matchesSearch =
      inv.invoiceNumber.toLowerCase().includes(searchTerm.toLowerCase()) ||
      inv.purchaseOrderNumber.toLowerCase().includes(searchTerm.toLowerCase()) ||
      inv.supplierName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      inv.supplierCode.toLowerCase().includes(searchTerm.toLowerCase());

    const matchesStatus =
      statusFilter === 'ALL' || inv.status === statusFilter;

    return matchesSearch && matchesStatus;
  });

  const counts = {
    total: invoices?.length || 0,
    pending: invoices?.filter((i) => i.status === 'PendingReconciliation').length || 0,
    matched: invoices?.filter((i) => i.status === 'Matched' || i.status === 'ApprovedWithVariance').length || 0,
    discrepancy: invoices?.filter((i) => i.status === 'Discrepancy').length || 0,
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Supplier Invoices</h1>
          <p className="text-sm text-slate-500 mt-1">
            Accounts payable invoices awaiting or processed through 3-way reconciliation against PO & Goods Receipts.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => setIsCsvOpen(true)} className="flex items-center gap-1.5">
            <Upload className="w-4 h-4 text-slate-500" />
            <span>Import CSV</span>
          </Button>
          <Button variant="primary" size="sm" onClick={() => setIsCreateOpen(true)} className="flex items-center gap-1.5">
            <Plus className="w-4 h-4" />
            <span>New Invoice</span>
          </Button>
        </div>
      </div>

      {/* Metric Cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <Card>
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs font-medium text-slate-500 uppercase tracking-wider">Total Invoices</p>
              <p className="text-2xl font-bold text-slate-900 mt-1">{counts.total}</p>
            </div>
            <div className="w-10 h-10 rounded-lg bg-blue-50 flex items-center justify-center text-blue-600">
              <Receipt className="w-5 h-5" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs font-medium text-slate-500 uppercase tracking-wider">Pending Reconcile</p>
              <p className="text-2xl font-bold text-amber-600 mt-1">{counts.pending}</p>
            </div>
            <div className="w-10 h-10 rounded-lg bg-amber-50 flex items-center justify-center text-amber-600">
              <Clock className="w-5 h-5" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs font-medium text-slate-500 uppercase tracking-wider">Matched / Approved</p>
              <p className="text-2xl font-bold text-emerald-600 mt-1">{counts.matched}</p>
            </div>
            <div className="w-10 h-10 rounded-lg bg-emerald-50 flex items-center justify-center text-emerald-600">
              <CheckCircle2 className="w-5 h-5" />
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs font-medium text-slate-500 uppercase tracking-wider">Discrepancies</p>
              <p className="text-2xl font-bold text-rose-600 mt-1">{counts.discrepancy}</p>
            </div>
            <div className="w-10 h-10 rounded-lg bg-rose-50 flex items-center justify-center text-rose-600">
              <AlertCircle className="w-5 h-5" />
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Filters & Search */}
      <Card>
        <CardContent className="p-4">
          <div className="flex flex-col sm:flex-row items-center gap-3">
            <div className="relative flex-1 w-full">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
              <input
                type="text"
                placeholder="Search by invoice #, PO #, supplier name, code..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="w-full pl-9 pr-4 py-2 border border-slate-200 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
            <div className="flex items-center gap-2 w-full sm:w-auto">
              <Filter className="w-4 h-4 text-slate-400" />
              <Select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="text-sm min-w-[180px]"
              >
                <option value="ALL">All Statuses</option>
                <option value="PendingReconciliation">Pending Reconciliation</option>
                <option value="Matched">Matched</option>
                <option value="Discrepancy">Discrepancy</option>
                <option value="ApprovedWithVariance">Approved With Variance</option>
                <option value="Rejected">Rejected</option>
              </Select>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Table */}
      <Card>
        <CardHeader className="border-b border-slate-100 pb-3">
          <CardTitle className="text-base font-semibold text-slate-900 flex items-center gap-2">
            <Receipt className="w-4 h-4 text-slate-500" />
            <span>Invoices Directory ({filteredInvoices.length})</span>
          </CardTitle>
        </CardHeader>
        <div className="overflow-x-auto">
          {isLoading ? (
            <div className="p-8 text-center text-sm text-slate-500">Loading supplier invoices...</div>
          ) : error ? (
            <div className="p-8 text-center text-sm text-rose-600">Failed to load invoices. Ensure OpsBridge.Api is running.</div>
          ) : filteredInvoices.length === 0 ? (
            <div className="p-12 text-center">
              <Receipt className="w-10 h-10 text-slate-300 mx-auto mb-3" />
              <p className="text-sm font-medium text-slate-900">No supplier invoices found</p>
              <p className="text-xs text-slate-500 mt-1 max-w-sm mx-auto">
                Create an invoice manually or import from CSV to initiate 3-way matching.
              </p>
              <Button
                variant="outline"
                size="sm"
                className="mt-4"
                onClick={() => setIsCreateOpen(true)}
              >
                <Plus className="w-4 h-4 mr-1.5" />
                Create Sample Invoice
              </Button>
            </div>
          ) : (
            <table className="w-full text-left text-sm text-slate-600">
              <thead className="bg-slate-50 text-xs uppercase font-semibold text-slate-500 border-b border-slate-100">
                <tr>
                  <th className="py-3 px-4">Invoice #</th>
                  <th className="py-3 px-4">PO Reference</th>
                  <th className="py-3 px-4">Supplier</th>
                  <th className="py-3 px-4 text-right">Subtotal</th>
                  <th className="py-3 px-4 text-right">Tax</th>
                  <th className="py-3 px-4 text-right">Total Amount</th>
                  <th className="py-3 px-4">Date</th>
                  <th className="py-3 px-4">Status</th>
                  <th className="py-3 px-4 text-right">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 font-normal">
                {filteredInvoices.map((inv) => (
                  <tr key={inv.id} className="hover:bg-slate-50/75 transition-colors">
                    <td className="py-3 px-4 font-mono font-medium text-slate-900">
                      {inv.invoiceNumber}
                    </td>
                    <td className="py-3 px-4 font-mono text-xs text-blue-600">
                      {inv.purchaseOrderNumber}
                    </td>
                    <td className="py-3 px-4">
                      <div className="font-medium text-slate-900">{inv.supplierName}</div>
                      <div className="text-xs font-mono text-slate-400">{inv.supplierCode}</div>
                    </td>
                    <td className="py-3 px-4 text-right font-mono">
                      {formatCurrency(inv.subTotal, inv.currency)}
                    </td>
                    <td className="py-3 px-4 text-right font-mono text-xs text-slate-500">
                      {formatCurrency(inv.taxAmount, inv.currency)}
                    </td>
                    <td className="py-3 px-4 text-right font-mono font-semibold text-slate-900">
                      {formatCurrency(inv.totalAmount, inv.currency)}
                    </td>
                    <td className="py-3 px-4 text-xs text-slate-500 whitespace-nowrap">
                      {formatDate(inv.invoiceDate)}
                    </td>
                    <td className="py-3 px-4">
                      <StatusBadge status={inv.status} />
                    </td>
                    <td className="py-3 px-4 text-right">
                      <div className="flex items-center justify-end gap-1.5">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setSelectedInvoice(inv)}
                          title="View Lines"
                          className="h-8 w-8 p-0"
                        >
                          <Eye className="w-4 h-4 text-slate-600" />
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => navigate(`/reconciliation?invoiceId=${inv.id}`)}
                          className="flex items-center gap-1 text-xs py-1 px-2.5 h-8 border-blue-200 text-blue-700 hover:bg-blue-50"
                        >
                          <Play className="w-3 h-3 text-blue-600" />
                          <span>3-Way Match</span>
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </Card>

      {/* Invoice Lines View Modal */}
      {selectedInvoice && (
        <Modal
          isOpen={!!selectedInvoice}
          onClose={() => setSelectedInvoice(null)}
          title={`Invoice Details: ${selectedInvoice.invoiceNumber}`}
          className="max-w-3xl"
        >
          <div className="space-y-4">
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 p-3 bg-slate-50 rounded-lg text-xs">
              <div>
                <span className="text-slate-500 block">Purchase Order:</span>
                <span className="font-mono font-medium text-slate-900">{selectedInvoice.purchaseOrderNumber}</span>
              </div>
              <div>
                <span className="text-slate-500 block">Supplier:</span>
                <span className="font-medium text-slate-900">{selectedInvoice.supplierName}</span>
              </div>
              <div>
                <span className="text-slate-500 block">Status:</span>
                <StatusBadge status={selectedInvoice.status} />
              </div>
              <div>
                <span className="text-slate-500 block">Total:</span>
                <span className="font-mono font-bold text-slate-900">
                  {formatCurrency(selectedInvoice.totalAmount, selectedInvoice.currency)}
                </span>
              </div>
            </div>

            <div>
              <h4 className="text-xs uppercase font-semibold text-slate-500 mb-2">Invoice Line Items</h4>
              <table className="w-full text-left text-xs text-slate-600 border border-slate-200 rounded-md overflow-hidden">
                <thead className="bg-slate-100 font-semibold text-slate-700">
                  <tr>
                    <th className="p-2.5">Line</th>
                    <th className="p-2.5">SKU</th>
                    <th className="p-2.5">Description</th>
                    <th className="p-2.5 text-right">Invoiced Qty</th>
                    <th className="p-2.5 text-right">Unit Price</th>
                    <th className="p-2.5 text-right">Tax Rate</th>
                    <th className="p-2.5 text-right">Line Total</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-200">
                  {selectedInvoice.lines.map((l) => (
                    <tr key={l.id}>
                      <td className="p-2.5 font-mono">{l.lineNumber}</td>
                      <td className="p-2.5 font-mono font-semibold text-slate-900">{l.itemSku}</td>
                      <td className="p-2.5 text-slate-500">{l.description || '—'}</td>
                      <td className="p-2.5 text-right font-mono font-medium text-slate-900">
                        {formatQuantity(l.invoicedQuantity, l.unitOfMeasure)}
                      </td>
                      <td className="p-2.5 text-right font-mono">
                        {formatCurrency(l.unitPrice, selectedInvoice.currency)}
                      </td>
                      <td className="p-2.5 text-right font-mono text-slate-500">
                        {(l.taxRate * 100).toFixed(0)}%
                      </td>
                      <td className="p-2.5 text-right font-mono font-semibold text-slate-900">
                        {formatCurrency(l.lineTotal, selectedInvoice.currency)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex justify-between items-center pt-3 border-t border-slate-100">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setSelectedInvoice(null)}
              >
                Close
              </Button>
              <Button
                variant="primary"
                size="sm"
                onClick={() => {
                  const id = selectedInvoice.id;
                  setSelectedInvoice(null);
                  navigate(`/reconciliation?invoiceId=${id}`);
                }}
                className="flex items-center gap-1.5"
              >
                <Play className="w-4 h-4" />
                <span>Launch 3-Way Match</span>
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {/* Create Modal */}
      <Modal
        isOpen={isCreateOpen}
        onClose={() => setIsCreateOpen(false)}
        title="Create Supplier Invoice"
        className="max-w-2xl"
      >
        <form onSubmit={handleCreateSubmit} className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="text-xs font-medium text-slate-700 block mb-1">Invoice Number *</label>
              <Input
                required
                value={invoiceNumber}
                onChange={(e) => setInvoiceNumber(e.target.value)}
                placeholder="e.g. INV-2026-901"
              />
            </div>
            <div>
              <label className="text-xs font-medium text-slate-700 block mb-1">PO Reference *</label>
              <Input
                required
                value={purchaseOrderNumber}
                onChange={(e) => setPurchaseOrderNumber(e.target.value)}
                placeholder="e.g. PO-2026-1001"
              />
            </div>
          </div>

          <div className="grid grid-cols-3 gap-4">
            <div>
              <label className="text-xs font-medium text-slate-700 block mb-1">Supplier Code *</label>
              <Input
                required
                value={supplierCode}
                onChange={(e) => setSupplierCode(e.target.value)}
                placeholder="e.g. SUP-ACME-01"
              />
            </div>
            <div>
              <label className="text-xs font-medium text-slate-700 block mb-1">Supplier Name *</label>
              <Input
                required
                value={supplierName}
                onChange={(e) => setSupplierName(e.target.value)}
                placeholder="e.g. Acme Industrial Supply"
              />
            </div>
            <div>
              <label className="text-xs font-medium text-slate-700 block mb-1">Currency</label>
              <Select value={currency} onChange={(e) => setCurrency(e.target.value)}>
                <option value="VND">VND (₫)</option>
                <option value="USD">USD ($)</option>
                <option value="EUR">EUR (€)</option>
              </Select>
            </div>
          </div>

          <div className="p-3 border border-slate-200 rounded-lg space-y-3 bg-slate-50">
            <h4 className="text-xs font-semibold text-slate-700 uppercase tracking-wider">Line Item #1</h4>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-xs font-medium text-slate-600 block mb-1">Item SKU *</label>
                <Input
                  required
                  value={sku}
                  onChange={(e) => setSku(e.target.value)}
                  placeholder="e.g. RM-008"
                />
              </div>
              <div>
                <label className="text-xs font-medium text-slate-600 block mb-1">Description</label>
                <Input
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Item description"
                />
              </div>
            </div>

            <div className="grid grid-cols-3 gap-3">
              <div>
                <label className="text-xs font-medium text-slate-600 block mb-1">Invoiced Qty *</label>
                <Input
                  type="number"
                  step="any"
                  required
                  value={quantity}
                  onChange={(e) => setQuantity(e.target.value)}
                />
              </div>
              <div>
                <label className="text-xs font-medium text-slate-600 block mb-1">Unit Price *</label>
                <Input
                  type="number"
                  step="any"
                  required
                  value={unitPrice}
                  onChange={(e) => setUnitPrice(e.target.value)}
                />
              </div>
              <div>
                <label className="text-xs font-medium text-slate-600 block mb-1">Tax Rate (e.g. 0.10)</label>
                <Input
                  type="number"
                  step="0.01"
                  value={taxRate}
                  onChange={(e) => setTaxRate(e.target.value)}
                />
              </div>
            </div>
          </div>

          {createMutation.isError && (
            <div className="p-3 bg-rose-50 border border-rose-200 rounded text-xs text-rose-700">
              {(createMutation.error as Error).message}
            </div>
          )}

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="outline" onClick={() => setIsCreateOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" disabled={createMutation.isPending}>
              {createMutation.isPending ? 'Creating...' : 'Create Invoice'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* CSV Import Modal */}
      <Modal
        isOpen={isCsvOpen}
        onClose={() => setIsCsvOpen(false)}
        title="Import Invoices via CSV"
        className="max-w-2xl"
      >
        <div className="space-y-4">
          <p className="text-xs text-slate-500">
            Paste CSV data containing invoice headers and line items. The backend validates required columns, computes subtotal and VAT, and saves to PostgreSQL.
          </p>
          <textarea
            rows={8}
            className="w-full font-mono text-xs p-3 border border-slate-200 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
            value={csvContent}
            onChange={(e) => setCsvContent(e.target.value)}
          />

          {csvMutation.isError && (
            <div className="p-3 bg-rose-50 border border-rose-200 rounded text-xs text-rose-700">
              {(csvMutation.error as Error).message}
            </div>
          )}

          <div className="flex justify-end gap-2">
            <Button variant="outline" onClick={() => setIsCsvOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="primary"
              onClick={() => csvMutation.mutate(csvContent)}
              disabled={csvMutation.isPending}
            >
              {csvMutation.isPending ? 'Importing...' : 'Upload & Parse CSV'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
