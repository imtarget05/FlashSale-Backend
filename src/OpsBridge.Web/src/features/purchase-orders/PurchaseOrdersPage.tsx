import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../lib/api-client';
import { Card, CardContent, CardHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Modal } from '../../components/ui/Modal';
import { formatCurrency, formatQuantity, formatDate } from '../../lib/formatters';
import type { PurchaseOrder } from '../../types';
import { FileText, Plus, Search, Eye, Upload, AlertCircle } from 'lucide-react';

export function PurchaseOrdersPage() {
  const queryClient = useQueryClient();
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedPo, setSelectedPo] = useState<PurchaseOrder | null>(null);
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [isCsvOpen, setIsCsvOpen] = useState(false);

  // New PO Form State
  const [orderNumber, setOrderNumber] = useState('PO-2026-1001');
  const [supplierCode, setSupplierCode] = useState('SUP-ACME-01');
  const [supplierName, setSupplierName] = useState('Acme Industrial Supply');
  const [currency, setCurrency] = useState('VND');
  const [sku, setSku] = useState('RM-008');
  const [description, setDescription] = useState('Raw Steel Rods 10mm');
  const [quantity, setQuantity] = useState('100');
  const [unitPrice, setUnitPrice] = useState('20000');
  const [csvContent, setCsvContent] = useState(
`OrderNumber,SupplierCode,SupplierName,Currency,LineNumber,ItemSku,Description,OrderedQuantity,UnitOfMeasure,UnitPrice
PO-2026-1001,SUP-ACME-01,Acme Industrial Supply,VND,1,RM-008,Raw Steel Rods 10mm,100,PCS,20000`
  );

  const { data: pos, isLoading, error } = useQuery({
    queryKey: ['purchase-orders'],
    queryFn: () => api.getPurchaseOrders(),
  });

  const createMutation = useMutation({
    mutationFn: (data: any) => api.createPurchaseOrder(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['purchase-orders'] });
      setIsCreateOpen(false);
    },
  });

  const csvMutation = useMutation({
    mutationFn: (csv: string) => api.importPurchaseOrdersCsv(csv),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['purchase-orders'] });
      setIsCsvOpen(false);
    },
  });

  const filteredPos = (pos || []).filter(
    (p) =>
      p.orderNumber.toLowerCase().includes(searchTerm.toLowerCase()) ||
      p.supplierName.toLowerCase().includes(searchTerm.toLowerCase()) ||
      p.supplierCode.toLowerCase().includes(searchTerm.toLowerCase())
  );

  const handleCreateSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    createMutation.mutate({
      orderNumber,
      supplierCode,
      supplierName,
      currency,
      lines: [
        {
          lineNumber: 1,
          itemSku: sku,
          description,
          orderedQuantity: parseFloat(quantity) || 0,
          unitOfMeasure: 'PCS',
          unitPrice: parseFloat(unitPrice) || 0,
        },
      ],
    });
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Purchase Orders</h1>
          <p className="text-sm text-slate-500 mt-1">
            Authoritative procurement baseline for lines, quantities, and agreed prices.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => setIsCsvOpen(true)} className="gap-1.5">
            <Upload className="w-4 h-4" />
            <span>Import CSV</span>
          </Button>
          <Button variant="primary" size="sm" onClick={() => setIsCreateOpen(true)} className="gap-1.5">
            <Plus className="w-4 h-4" />
            <span>New Purchase Order</span>
          </Button>
        </div>
      </div>

      {/* Filter and Table */}
      <Card>
        <CardHeader className="py-3 px-4 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
          <div className="relative w-full sm:w-80">
            <Search className="w-4 h-4 absolute left-3 top-2.5 text-slate-400" />
            <input
              type="text"
              placeholder="Search by PO Number or Supplier..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full pl-9 pr-3 py-1.5 text-xs bg-slate-50 border border-slate-200 rounded-lg focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div className="text-xs text-slate-500 font-medium">
            Showing {filteredPos.length} purchase orders
          </div>
        </CardHeader>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="p-12 text-center text-xs text-slate-500">Loading purchase orders...</div>
          ) : error ? (
            <div className="p-8 text-center text-xs text-rose-600 flex items-center justify-center gap-2">
              <AlertCircle className="w-4 h-4" />
              <span>Failed to load purchase orders: {(error as Error).message}</span>
            </div>
          ) : filteredPos.length === 0 ? (
            <div className="p-12 text-center text-xs text-slate-400 space-y-2">
              <FileText className="w-8 h-8 mx-auto text-slate-300" />
              <p>No purchase orders found.</p>
              <Button variant="outline" size="sm" onClick={() => setIsCreateOpen(true)}>
                Create First PO
              </Button>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs">
                <thead className="bg-slate-50 border-b border-slate-200 text-slate-600 font-semibold uppercase tracking-wider">
                  <tr>
                    <th className="py-3 px-4">PO Number</th>
                    <th className="py-3 px-4">Supplier</th>
                    <th className="py-3 px-4">Currency</th>
                    <th className="py-3 px-4">Lines</th>
                    <th className="py-3 px-4">Total Amount</th>
                    <th className="py-3 px-4">Created Date</th>
                    <th className="py-3 px-4 text-right">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 text-slate-700">
                  {filteredPos.map((po) => (
                    <tr key={po.id} className="hover:bg-slate-50/80 transition-colors">
                      <td className="py-3 px-4 font-bold text-slate-900 font-mono">
                        {po.orderNumber}
                      </td>
                      <td className="py-3 px-4">
                        <div className="font-medium text-slate-900">{po.supplierName}</div>
                        <div className="text-[10px] text-slate-400 font-mono">{po.supplierCode}</div>
                      </td>
                      <td className="py-3 px-4 font-mono font-semibold">{po.currency}</td>
                      <td className="py-3 px-4 font-medium">{po.lines?.length || 0} items</td>
                      <td className="py-3 px-4 font-semibold text-slate-900">
                        {formatCurrency(po.totalAmount, po.currency)}
                      </td>
                      <td className="py-3 px-4 text-slate-500">{formatDate(po.orderedAt)}</td>
                      <td className="py-3 px-4 text-right">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setSelectedPo(po)}
                          className="h-7 px-2 text-indigo-600 hover:text-indigo-800 hover:bg-indigo-50"
                        >
                          <Eye className="w-3.5 h-3.5 mr-1" />
                          <span>View</span>
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Detail Modal */}
      {selectedPo && (
        <Modal
          isOpen={!!selectedPo}
          onClose={() => setSelectedPo(null)}
          title={`Purchase Order: ${selectedPo.orderNumber}`}
          description={`Supplier: ${selectedPo.supplierName} (${selectedPo.supplierCode}) | Currency: ${selectedPo.currency}`}
          maxWidth="2xl"
        >
          <div className="space-y-4">
            <div className="border border-slate-200 rounded-xl overflow-hidden">
              <table className="w-full text-left text-xs">
                <thead className="bg-slate-50 border-b border-slate-200 text-slate-600 font-semibold uppercase">
                  <tr>
                    <th className="py-2.5 px-3">#</th>
                    <th className="py-2.5 px-3">SKU</th>
                    <th className="py-2.5 px-3">Description</th>
                    <th className="py-2.5 px-3 text-right">Ordered Qty</th>
                    <th className="py-2.5 px-3 text-right">Unit Price</th>
                    <th className="py-2.5 px-3 text-right">Total</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {selectedPo.lines?.map((line) => (
                    <tr key={line.id}>
                      <td className="py-2.5 px-3 font-mono text-slate-400">{line.lineNumber}</td>
                      <td className="py-2.5 px-3 font-mono font-bold text-slate-800">{line.itemSku}</td>
                      <td className="py-2.5 px-3 text-slate-600">{line.description || '-'}</td>
                      <td className="py-2.5 px-3 text-right font-medium">
                        {formatQuantity(line.orderedQuantity, line.unitOfMeasure)}
                      </td>
                      <td className="py-2.5 px-3 text-right font-mono">
                        {formatCurrency(line.unitPrice, selectedPo.currency)}
                      </td>
                      <td className="py-2.5 px-3 text-right font-bold text-slate-900 font-mono">
                        {formatCurrency(line.totalAmount, selectedPo.currency)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex justify-between items-center pt-2">
              <div className="text-xs text-slate-500">
                Created: {formatDate(selectedPo.orderedAt)} via {selectedPo.sourceSystem}
              </div>
              <div className="text-sm font-bold text-slate-900">
                Total: {formatCurrency(selectedPo.totalAmount, selectedPo.currency)}
              </div>
            </div>
          </div>
        </Modal>
      )}

      {/* Create Modal */}
      <Modal
        isOpen={isCreateOpen}
        onClose={() => setIsCreateOpen(false)}
        title="Create New Purchase Order"
        description="Enter procurement order parameters and line item specifications."
      >
        <form onSubmit={handleCreateSubmit} className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <Input
              label="PO Number"
              value={orderNumber}
              onChange={(e) => setOrderNumber(e.target.value)}
              required
            />
            <Input
              label="Currency"
              value={currency}
              onChange={(e) => setCurrency(e.target.value)}
              required
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <Input
              label="Supplier Code"
              value={supplierCode}
              onChange={(e) => setSupplierCode(e.target.value)}
              required
            />
            <Input
              label="Supplier Name"
              value={supplierName}
              onChange={(e) => setSupplierName(e.target.value)}
              required
            />
          </div>

          <div className="p-3 bg-slate-50 border border-slate-200 rounded-xl space-y-3">
            <div className="text-xs font-bold text-slate-700">Line Item 1</div>
            <div className="grid grid-cols-2 gap-3">
              <Input
                label="SKU"
                value={sku}
                onChange={(e) => setSku(e.target.value)}
                required
              />
              <Input
                label="Description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
              />
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Input
                label="Quantity (PCS)"
                type="number"
                value={quantity}
                onChange={(e) => setQuantity(e.target.value)}
                required
              />
              <Input
                label="Unit Price (VND)"
                type="number"
                value={unitPrice}
                onChange={(e) => setUnitPrice(e.target.value)}
                required
              />
            </div>
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="outline" onClick={() => setIsCreateOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" isLoading={createMutation.isPending}>
              Create PO
            </Button>
          </div>
        </form>
      </Modal>

      {/* CSV Import Modal */}
      <Modal
        isOpen={isCsvOpen}
        onClose={() => setIsCsvOpen(false)}
        title="Import Purchase Orders via CSV"
        description="Paste comma-separated procurement order lines directly."
      >
        <div className="space-y-4">
          <textarea
            value={csvContent}
            onChange={(e) => setCsvContent(e.target.value)}
            rows={6}
            className="w-full p-3 font-mono text-xs bg-slate-50 border border-slate-300 rounded-lg focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setIsCsvOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="primary"
              isLoading={csvMutation.isPending}
              onClick={() => csvMutation.mutate(csvContent)}
            >
              Parse & Import CSV
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
