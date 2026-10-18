import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../../lib/api-client';
import { Card, CardContent, CardHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Input } from '../../components/ui/Input';
import { Modal } from '../../components/ui/Modal';
import { formatQuantity, formatDate } from '../../lib/formatters';
import type { GoodsReceipt } from '../../types';
import { PackageCheck, Plus, Search, Eye, AlertCircle } from 'lucide-react';

export function GoodsReceiptsPage() {
  const queryClient = useQueryClient();
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedGr, setSelectedGr] = useState<GoodsReceipt | null>(null);
  const [isCreateOpen, setIsCreateOpen] = useState(false);

  // New GR Form State
  const [receiptNumber, setReceiptNumber] = useState('GR-2026-001');
  const [purchaseOrderNumber, setPurchaseOrderNumber] = useState('PO-2026-1001');
  const [supplierCode, setSupplierCode] = useState('SUP-ACME-01');
  const [sku, setSku] = useState('RM-008');
  const [receivedQty, setReceivedQty] = useState('60');

  const { data: grs, isLoading, error } = useQuery({
    queryKey: ['goods-receipts'],
    queryFn: () => api.getGoodsReceipts(),
  });

  const createMutation = useMutation({
    mutationFn: (data: any) => api.createGoodsReceipt(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['goods-receipts'] });
      setIsCreateOpen(false);
    },
  });

  const filteredGrs = (grs || []).filter(
    (g) =>
      g.receiptNumber.toLowerCase().includes(searchTerm.toLowerCase()) ||
      g.purchaseOrderNumber.toLowerCase().includes(searchTerm.toLowerCase()) ||
      g.supplierCode.toLowerCase().includes(searchTerm.toLowerCase())
  );

  const handleCreateSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    createMutation.mutate({
      receiptNumber,
      purchaseOrderNumber,
      supplierCode,
      lines: [
        {
          lineNumber: 1,
          itemSku: sku,
          receivedQuantity: parseFloat(receivedQty) || 0,
          unitOfMeasure: 'PCS',
        },
      ],
    });
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900">Goods Receipts</h1>
          <p className="text-sm text-slate-500 mt-1">
            Warehouse delivery confirmations received via FactoryFlow webhooks or manual receiving.
          </p>
        </div>
        <Button variant="primary" size="sm" onClick={() => setIsCreateOpen(true)} className="gap-1.5">
          <Plus className="w-4 h-4" />
          <span>Record Receipt</span>
        </Button>
      </div>

      {/* Filter and Table */}
      <Card>
        <CardHeader className="py-3 px-4 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
          <div className="relative w-full sm:w-80">
            <Search className="w-4 h-4 absolute left-3 top-2.5 text-slate-400" />
            <input
              type="text"
              placeholder="Search by GR Number or PO Reference..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full pl-9 pr-3 py-1.5 text-xs bg-slate-50 border border-slate-200 rounded-lg focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div className="text-xs text-slate-500 font-medium">
            Showing {filteredGrs.length} warehouse receipts
          </div>
        </CardHeader>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="p-12 text-center text-xs text-slate-500">Loading goods receipts...</div>
          ) : error ? (
            <div className="p-8 text-center text-xs text-rose-600 flex items-center justify-center gap-2">
              <AlertCircle className="w-4 h-4" />
              <span>Failed to load goods receipts: {(error as Error).message}</span>
            </div>
          ) : filteredGrs.length === 0 ? (
            <div className="p-12 text-center text-xs text-slate-400 space-y-2">
              <PackageCheck className="w-8 h-8 mx-auto text-slate-300" />
              <p>No goods receipts found.</p>
              <Button variant="outline" size="sm" onClick={() => setIsCreateOpen(true)}>
                Record First Delivery
              </Button>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs">
                <thead className="bg-slate-50 border-b border-slate-200 text-slate-600 font-semibold uppercase tracking-wider">
                  <tr>
                    <th className="py-3 px-4">Receipt Number</th>
                    <th className="py-3 px-4">PO Reference</th>
                    <th className="py-3 px-4">Supplier</th>
                    <th className="py-3 px-4">Received Date</th>
                    <th className="py-3 px-4">Source System</th>
                    <th className="py-3 px-4">Lines Received</th>
                    <th className="py-3 px-4 text-right">Action</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 text-slate-700">
                  {filteredGrs.map((gr) => {
                    const totalReceived = gr.lines?.reduce((s, l) => s + l.receivedQuantity, 0) || 0;
                    const totalAllocated = gr.lines?.reduce((s, l) => s + l.allocatedQuantity, 0) || 0;
                    return (
                      <tr key={gr.id} className="hover:bg-slate-50/80 transition-colors">
                        <td className="py-3 px-4 font-bold text-slate-900 font-mono">
                          {gr.receiptNumber}
                        </td>
                        <td className="py-3 px-4 font-mono font-semibold text-indigo-700">
                          {gr.purchaseOrderNumber}
                        </td>
                        <td className="py-3 px-4 font-mono text-slate-600">{gr.supplierCode}</td>
                        <td className="py-3 px-4 text-slate-500">{formatDate(gr.receivedAt)}</td>
                        <td className="py-3 px-4">
                          <span className="inline-flex items-center px-2 py-0.5 rounded text-[10px] font-medium bg-slate-100 text-slate-700 font-mono">
                            {gr.sourceSystem}
                          </span>
                        </td>
                        <td className="py-3 px-4 font-medium">
                          <span>{totalReceived} PCS</span>
                          <span className="text-[10px] text-slate-400 ml-1.5">
                            ({totalAllocated} allocated)
                          </span>
                        </td>
                        <td className="py-3 px-4 text-right">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setSelectedGr(gr)}
                            className="h-7 px-2 text-indigo-600 hover:text-indigo-800 hover:bg-indigo-50"
                          >
                            <Eye className="w-3.5 h-3.5 mr-1" />
                            <span>View</span>
                          </Button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Detail Modal */}
      {selectedGr && (
        <Modal
          isOpen={!!selectedGr}
          onClose={() => setSelectedGr(null)}
          title={`Goods Receipt: ${selectedGr.receiptNumber}`}
          description={`PO Reference: ${selectedGr.purchaseOrderNumber} | Supplier: ${selectedGr.supplierCode}`}
          maxWidth="2xl"
        >
          <div className="space-y-4">
            <div className="border border-slate-200 rounded-xl overflow-hidden">
              <table className="w-full text-left text-xs">
                <thead className="bg-slate-50 border-b border-slate-200 text-slate-600 font-semibold uppercase">
                  <tr>
                    <th className="py-2.5 px-3">#</th>
                    <th className="py-2.5 px-3">SKU</th>
                    <th className="py-2.5 px-3 text-right">Received</th>
                    <th className="py-2.5 px-3 text-right">Allocated</th>
                    <th className="py-2.5 px-3 text-right">Available for AP</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {selectedGr.lines?.map((line) => (
                    <tr key={line.id}>
                      <td className="py-2.5 px-3 font-mono text-slate-400">{line.lineNumber}</td>
                      <td className="py-2.5 px-3 font-mono font-bold text-slate-800">{line.itemSku}</td>
                      <td className="py-2.5 px-3 text-right font-medium">
                        {formatQuantity(line.receivedQuantity, line.unitOfMeasure)}
                      </td>
                      <td className="py-2.5 px-3 text-right font-medium text-indigo-600">
                        {formatQuantity(line.allocatedQuantity, line.unitOfMeasure)}
                      </td>
                      <td className="py-2.5 px-3 text-right font-bold text-emerald-700">
                        {formatQuantity(line.availableQuantity, line.unitOfMeasure)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex justify-between items-center pt-2 text-xs text-slate-500">
              <div>Received: {formatDate(selectedGr.receivedAt)}</div>
              {selectedGr.sourceEventId && (
                <div className="font-mono text-[10px]">
                  Event ID: {selectedGr.sourceEventId}
                </div>
              )}
            </div>
          </div>
        </Modal>
      )}

      {/* Create Modal */}
      <Modal
        isOpen={isCreateOpen}
        onClose={() => setIsCreateOpen(false)}
        title="Record Goods Receipt Delivery"
        description="Simulate warehouse intake or record manual partial shipments."
      >
        <form onSubmit={handleCreateSubmit} className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <Input
              label="Receipt Number (GR)"
              value={receiptNumber}
              onChange={(e) => setReceiptNumber(e.target.value)}
              required
            />
            <Input
              label="PO Number"
              value={purchaseOrderNumber}
              onChange={(e) => setPurchaseOrderNumber(e.target.value)}
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
              label="Item SKU"
              value={sku}
              onChange={(e) => setSku(e.target.value)}
              required
            />
          </div>

          <div>
            <Input
              label="Received Quantity (PCS)"
              type="number"
              value={receivedQty}
              onChange={(e) => setReceivedQty(e.target.value)}
              required
            />
          </div>

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="outline" onClick={() => setIsCreateOpen(false)}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" isLoading={createMutation.isPending}>
              Record Receipt
            </Button>
          </div>
        </form>
      </Modal>
    </div>
  );
}
