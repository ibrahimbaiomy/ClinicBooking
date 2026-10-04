namespace ClinicBooking.Api.Authorization;

/// <summary>
/// Lets a request through while the caller still has a temporary password (D57). Only
/// <c>GET /api/auth/me</c> and <c>POST /api/auth/change-password</c> carry it; a test fails if it
/// appears anywhere else. Methods only, so it can never be put on a whole controller.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AllowWhilePasswordChangeRequiredAttribute : Attribute;
