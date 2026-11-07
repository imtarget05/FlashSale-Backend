package com.flashsale.payment.provider;

import com.flashsale.payment.dto.ProcessPaymentRequest;
import com.flashsale.payment.dto.PaymentRecordDto;

/**
 * Cổng thanh toán (PSP port). Mọi tương tác với nhà cung cấp thanh toán bên ngoài
 * phải đi qua giao diện này. Adapter hiện tại {@link SimulatedPaymentProvider}
 * mô phỏng kết quả bằng quy tắc cents (.01 thành công, .02 từ chối, .03 hết thời gian)
 * để chạy cục bộ / demo — KHÔNG phải PSP thật.
 *
 * <p>Khi có PSP thật, chỉ cần thêm một adapter mới triển khai {@link PaymentProvider}
 * và wiring qua Spring bean (hoặc @Profile) mà không động lớp nghiệp vụ
 * {@link com.flashsale.payment.service.PaymentService}.
 */
public interface PaymentProvider {

	/**
	 * Xử lý một yêu cầu thanh toán. Trả về DTO chứa kết quả (success/declined/timed-out)
	 * mà không tự ném ngoại lệ cho kết quả nghiệp vụ thông thường.
	 *
	 * @param request yêu cầu thanh toán đã xác thực idempotency key
	 * @return kết quả xử lý từ PSP
	 */
	PaymentRecordDto process(ProcessPaymentRequest request);
}
