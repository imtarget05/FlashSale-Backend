export function formatCurrency(amount: number, currency = 'VND'): string {
  if (currency === 'VND') {
    return new Intl.NumberFormat('vi-VN', {
      style: 'currency',
      currency: 'VND',
      maximumFractionDigits: 0,
    }).format(amount);
  }

  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
}

export function formatQuantity(quantity: number, uom = 'PCS'): string {
  const formatted = new Intl.NumberFormat('en-US', {
    maximumFractionDigits: 2,
  }).format(quantity);
  return `${formatted} ${uom}`;
}

export function formatDate(dateString: string): string {
  if (!dateString) return '-';
  try {
    const d = new Date(dateString);
    return new Intl.DateTimeFormat('vi-VN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    }).format(d);
  } catch {
    return dateString;
  }
}

export function formatPercent(value: number): string {
  return `${(value * 100).toFixed(1)}%`;
}
