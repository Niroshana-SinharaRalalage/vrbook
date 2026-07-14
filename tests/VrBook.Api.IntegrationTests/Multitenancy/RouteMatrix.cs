using System.Net;
using System.Net.Http.Json;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 §4.4 (D4) Step 2 — single-source-of-truth
/// enumeration of every authenticated endpoint × persona × tenant
/// combination. Drives the <see cref="CrossTenantEndpointMatrix"/>
/// <c>[Theory]</c> via <c>[MemberData]</c>.
///
/// <para>Adding a new endpoint to the platform = adding new yield rows
/// here. The Wave 1 <c>EndpointCoverageArchTest</c> guards that every
/// new controller action carries an explicit access decision; once the
/// Wave 2 second-half arch enforcement lights up, this matrix becomes
/// the contract.</para>
/// </summary>
public static class RouteMatrix
{
    public enum Persona
    {
        OwnerA,
        OwnerB,
        PlatformAdmin,
        Anonymous,
    }

    public enum TargetTenant
    {
        A,
        B,
        None,
    }

    /// <summary>One matrix row.</summary>
    public sealed record Cell(
        string Description,
        string Verb,
        string Route,
        Persona Persona,
        TargetTenant Target,
        HttpStatusCode[] AcceptedStatuses,
        Func<HttpRequestMessage>? BodyFactory = null)
    {
        // xUnit uses this as the test display name via [MemberData].
        public override string ToString() => Description;
    }

    private static HttpStatusCode[] Ok =>
        new[] { HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created, HttpStatusCode.Accepted };

    private static HttpStatusCode[] Forbidden =>
        new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound };

    private static HttpStatusCode[] Unauthorized =>
        new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Redirect, HttpStatusCode.Found };

    private static Func<HttpRequestMessage> JsonBody(object payload) =>
        () =>
        {
            var req = new HttpRequestMessage();
            req.Content = JsonContent.Create(payload);
            return req;
        };

    // VRB-300 helpers. The authorization pipeline (authentication challenge,
    // then the role/tenant gate) runs BEFORE model binding and the handler, so
    // an anonymous or wrong-persona request is rejected without a body and
    // without the route's resource existing. These rows assert exactly that
    // "who may reach this endpoint" contract; the happy-path body, validation,
    // error-contract, idempotency and per-resource cross-tenant isolation live
    // in the per-module Contract/* classes.

    /// <summary>Anonymous caller on an authenticated endpoint → rejected (401/403/redirect).</summary>
    private static Cell Anon(string description, string verb, string route) =>
        new(description, verb, route, Persona.Anonymous, TargetTenant.None, Unauthorized);

    /// <summary>A non-platform-admin (tenant owner) on a PlatformAdmin-only endpoint → 403.</summary>
    private static Cell OwnerRejected(string description, string verb, string route, Persona owner) =>
        new(description, verb, route, owner, TargetTenant.None, Forbidden);

    public static IEnumerable<object[]> GetAll()
    {
        foreach (var cell in Build())
        {
            yield return new object[] { cell };
        }
    }

    private static IEnumerable<Cell> Build()
    {
        // ====================================================================
        // M.7 + Identity — /api/v1/me + /api/v1/me/tenant
        // ====================================================================
        // OwnerA / OwnerB get their own tenant. PlatformAdmin has NO
        // membership (per fixture seed) so /me/tenant returns 403/404.
        // Anonymous returns 401/403.
        yield return new Cell("OwnerA_GET_me_returns_200",
            "GET", "/api/v1/me", Persona.OwnerA, TargetTenant.None, Ok);
        yield return new Cell("OwnerB_GET_me_returns_200",
            "GET", "/api/v1/me", Persona.OwnerB, TargetTenant.None, Ok);
        yield return new Cell("PlatformAdmin_GET_me_returns_200",
            "GET", "/api/v1/me", Persona.PlatformAdmin, TargetTenant.None, Ok);
        yield return new Cell("Anonymous_GET_me_returns_401",
            "GET", "/api/v1/me", Persona.Anonymous, TargetTenant.None, Unauthorized);

        yield return new Cell("OwnerA_GET_me_tenant_returns_tenantA",
            "GET", "/api/v1/me/tenant", Persona.OwnerA, TargetTenant.A, Ok);
        yield return new Cell("OwnerB_GET_me_tenant_returns_tenantB",
            "GET", "/api/v1/me/tenant", Persona.OwnerB, TargetTenant.B, Ok);
        yield return new Cell("PlatformAdmin_GET_me_tenant_returns_403_or_404_no_membership",
            "GET", "/api/v1/me/tenant", Persona.PlatformAdmin, TargetTenant.None,
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        yield return new Cell("Anonymous_GET_me_tenant_returns_401",
            "GET", "/api/v1/me/tenant", Persona.Anonymous, TargetTenant.None, Unauthorized);

        // ====================================================================
        // M.5 — /api/v1/admin/tenants/{tenantId}/stripe/* (TenantsAdminController)
        // ====================================================================
        // The {tenantId} route segment is gated by TenantAuthorizationBehavior
        // (M.4). Owner-of-A can call own tenant's onboard; Owner-of-A cannot
        // call tenant-B's onboard (cross-tenant 403/404).
        // PlatformAdmin can NOT use this surface (TenantsAdminController gates
        // on Roles="Owner,Admin", not PlatformAdmin) — these endpoints are
        // tenant-self-service, not platform-wide.
        foreach (var (tenant, ownerPersona) in new[]
        {
            (TargetTenant.A, Persona.OwnerA),
            (TargetTenant.B, Persona.OwnerB),
        })
        {
            var route = $"/api/v1/admin/tenants/{{tenantId}}/stripe/onboard";
            var oppositeOwner = ownerPersona == Persona.OwnerA ? Persona.OwnerB : Persona.OwnerA;
            yield return new Cell(
                $"Owner_{ownerPersona}_POST_stripe_onboard_for_own_tenant_{tenant}_passes_or_returns_502",
                "POST", route, ownerPersona, tenant,
                new[] { HttpStatusCode.OK, HttpStatusCode.BadGateway, HttpStatusCode.UnprocessableEntity },
                JsonBody(new { country = "US" }));
            yield return new Cell(
                $"Owner_{oppositeOwner}_POST_stripe_onboard_for_cross_tenant_{tenant}_rejected",
                "POST", route, oppositeOwner, tenant, Forbidden,
                JsonBody(new { country = "US" }));
        }

        // account-link path
        foreach (var (tenant, ownerPersona) in new[]
        {
            (TargetTenant.A, Persona.OwnerA),
            (TargetTenant.B, Persona.OwnerB),
        })
        {
            var oppositeOwner = ownerPersona == Persona.OwnerA ? Persona.OwnerB : Persona.OwnerA;
            yield return new Cell(
                $"Owner_{oppositeOwner}_POST_stripe_account_link_for_cross_tenant_{tenant}_rejected",
                "POST", "/api/v1/admin/tenants/{tenantId}/stripe/account-link",
                oppositeOwner, tenant, Forbidden);
        }

        // login-link path
        foreach (var (tenant, ownerPersona) in new[]
        {
            (TargetTenant.A, Persona.OwnerA),
            (TargetTenant.B, Persona.OwnerB),
        })
        {
            var oppositeOwner = ownerPersona == Persona.OwnerA ? Persona.OwnerB : Persona.OwnerA;
            yield return new Cell(
                $"Owner_{oppositeOwner}_POST_stripe_login_link_for_cross_tenant_{tenant}_rejected",
                "POST", "/api/v1/admin/tenants/{tenantId}/stripe/login-link",
                oppositeOwner, tenant, Forbidden);
        }

        // Anonymous on any stripe path is rejected.
        yield return new Cell("Anonymous_POST_stripe_onboard_returns_401",
            "POST", "/api/v1/admin/tenants/{tenantId}/stripe/onboard",
            Persona.Anonymous, TargetTenant.A, Unauthorized,
            JsonBody(new { country = "US" }));

        // ====================================================================
        // M.8 — /api/v1/admin/platform/tenants/* (TenantsPlatformController)
        // ====================================================================
        // [Authorize(Roles="PlatformAdmin")] — only PlatformAdmin passes.
        // OwnerA / OwnerB / Anonymous all rejected with 403/401.
        yield return new Cell("PlatformAdmin_GET_platform_tenants_list_returns_200",
            "GET", "/api/v1/admin/platform/tenants",
            Persona.PlatformAdmin, TargetTenant.None, Ok);
        yield return new Cell("OwnerA_GET_platform_tenants_list_returns_403",
            "GET", "/api/v1/admin/platform/tenants",
            Persona.OwnerA, TargetTenant.None, Forbidden);
        yield return new Cell("OwnerB_GET_platform_tenants_list_returns_403",
            "GET", "/api/v1/admin/platform/tenants",
            Persona.OwnerB, TargetTenant.None, Forbidden);
        yield return new Cell("Anonymous_GET_platform_tenants_list_returns_401",
            "GET", "/api/v1/admin/platform/tenants",
            Persona.Anonymous, TargetTenant.None, Unauthorized);

        foreach (var tenant in new[] { TargetTenant.A, TargetTenant.B })
        {
            yield return new Cell(
                $"PlatformAdmin_GET_platform_tenant_detail_{tenant}_returns_200",
                "GET", "/api/v1/admin/platform/tenants/{tenantId}",
                Persona.PlatformAdmin, tenant, Ok);
            yield return new Cell(
                $"OwnerA_GET_platform_tenant_detail_{tenant}_returns_403",
                "GET", "/api/v1/admin/platform/tenants/{tenantId}",
                Persona.OwnerA, tenant, Forbidden);
            yield return new Cell(
                $"OwnerB_GET_platform_tenant_detail_{tenant}_returns_403",
                "GET", "/api/v1/admin/platform/tenants/{tenantId}",
                Persona.OwnerB, tenant, Forbidden);
            yield return new Cell(
                $"Anonymous_GET_platform_tenant_detail_{tenant}_returns_401",
                "GET", "/api/v1/admin/platform/tenants/{tenantId}",
                Persona.Anonymous, tenant, Unauthorized);

            // Suspend / Reactivate / SetFee — only PlatformAdmin passes
            yield return new Cell(
                $"OwnerA_POST_platform_suspend_{tenant}_returns_403",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/suspend",
                Persona.OwnerA, tenant, Forbidden,
                JsonBody(new { reason = "test" }));
            yield return new Cell(
                $"OwnerB_POST_platform_suspend_{tenant}_returns_403",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/suspend",
                Persona.OwnerB, tenant, Forbidden,
                JsonBody(new { reason = "test" }));
            yield return new Cell(
                $"Anonymous_POST_platform_suspend_{tenant}_returns_401",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/suspend",
                Persona.Anonymous, tenant, Unauthorized,
                JsonBody(new { reason = "test" }));

            yield return new Cell(
                $"OwnerA_POST_platform_reactivate_{tenant}_returns_403",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/reactivate",
                Persona.OwnerA, tenant, Forbidden);
            yield return new Cell(
                $"OwnerB_POST_platform_reactivate_{tenant}_returns_403",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/reactivate",
                Persona.OwnerB, tenant, Forbidden);

            yield return new Cell(
                $"OwnerA_PUT_platform_fee_{tenant}_returns_403",
                "PUT", "/api/v1/admin/platform/tenants/{tenantId}/platform-fee",
                Persona.OwnerA, tenant, Forbidden,
                JsonBody(new { bps = 2000 }));
            yield return new Cell(
                $"OwnerB_PUT_platform_fee_{tenant}_returns_403",
                "PUT", "/api/v1/admin/platform/tenants/{tenantId}/platform-fee",
                Persona.OwnerB, tenant, Forbidden,
                JsonBody(new { bps = 2000 }));
        }

        // ====================================================================
        // VRB-300 — full authenticated-endpoint coverage (auth dimension).
        // Every authenticated controller action gets its "authentication is
        // required" row here so the strengthened EndpointCoverageArchTest
        // (matrix-membership-or-exempt) passes without blanket exemptions.
        // PlatformAdmin-only and {tenantId}-scoped surfaces additionally get
        // their role/cross-tenant rejection rows — those are guaranteed by the
        // gate, not by business logic, so they are safe to assert here.
        // ====================================================================

        // ---- Identity — /api/v1/me (mutations + tenant picker) ----
        yield return Anon("Anonymous_PUT_me_returns_401", "PUT", "/api/v1/me");
        yield return Anon("Anonymous_DELETE_me_returns_401", "DELETE", "/api/v1/me");
        yield return Anon("Anonymous_GET_me_tenants_returns_401", "GET", "/api/v1/me/tenants");
        yield return Anon("Anonymous_GET_me_loyalty_returns_401", "GET", "/api/v1/me/loyalty");

        // ---- Catalog — properties (owner writes) ----
        yield return Anon("Anonymous_POST_property_returns_401", "POST", "/api/v1/properties");
        yield return Anon("Anonymous_PUT_property_returns_401", "PUT", "/api/v1/properties/{id}");
        yield return Anon("Anonymous_POST_property_image_returns_401", "POST", "/api/v1/properties/{id}/images");
        yield return Anon("Anonymous_PUT_property_image_order_returns_401", "PUT", "/api/v1/properties/{id}/images/order");
        yield return Anon("Anonymous_DELETE_property_image_returns_401", "DELETE", "/api/v1/properties/{id}/images/{imageId}");

        // ---- Catalog — admin property read surface ----
        yield return Anon("Anonymous_GET_admin_properties_returns_401", "GET", "/api/v1/admin/properties");
        yield return Anon("Anonymous_GET_admin_property_detail_returns_401", "GET", "/api/v1/admin/properties/{id}");

        // ---- Catalog — admin amenity catalog (PlatformAdmin only) ----
        yield return Anon("Anonymous_GET_admin_amenities_returns_401", "GET", "/api/v1/admin/amenities");
        yield return new Cell("PlatformAdmin_GET_admin_amenities_returns_200",
            "GET", "/api/v1/admin/amenities", Persona.PlatformAdmin, TargetTenant.None, Ok);
        yield return OwnerRejected("OwnerA_GET_admin_amenities_returns_403", "GET", "/api/v1/admin/amenities", Persona.OwnerA);
        yield return OwnerRejected("OwnerB_GET_admin_amenities_returns_403", "GET", "/api/v1/admin/amenities", Persona.OwnerB);
        yield return Anon("Anonymous_POST_admin_amenity_returns_401", "POST", "/api/v1/admin/amenities");
        yield return OwnerRejected("OwnerA_POST_admin_amenity_returns_403", "POST", "/api/v1/admin/amenities", Persona.OwnerA);
        yield return Anon("Anonymous_PUT_admin_amenity_returns_401", "PUT", "/api/v1/admin/amenities/{id}");
        yield return OwnerRejected("OwnerA_PUT_admin_amenity_returns_403", "PUT", "/api/v1/admin/amenities/{id}", Persona.OwnerA);
        yield return Anon("Anonymous_POST_admin_amenity_disable_returns_401", "POST", "/api/v1/admin/amenities/{id}/disable");
        yield return OwnerRejected("OwnerA_POST_admin_amenity_disable_returns_403", "POST", "/api/v1/admin/amenities/{id}/disable", Persona.OwnerA);
        yield return Anon("Anonymous_POST_admin_amenity_enable_returns_401", "POST", "/api/v1/admin/amenities/{id}/enable");
        yield return OwnerRejected("OwnerA_POST_admin_amenity_enable_returns_403", "POST", "/api/v1/admin/amenities/{id}/enable", Persona.OwnerA);
        yield return Anon("Anonymous_DELETE_admin_amenity_returns_401", "DELETE", "/api/v1/admin/amenities/{id}");
        yield return OwnerRejected("OwnerA_DELETE_admin_amenity_returns_403", "DELETE", "/api/v1/admin/amenities/{id}", Persona.OwnerA);

        // ---- Booking — guest + owner actions on /api/v1/bookings ----
        yield return Anon("Anonymous_POST_hold_returns_401", "POST", "/api/v1/bookings/holds");
        yield return Anon("Anonymous_DELETE_hold_returns_401", "DELETE", "/api/v1/bookings/holds/{holdId}");
        yield return Anon("Anonymous_POST_booking_returns_401", "POST", "/api/v1/bookings");
        yield return Anon("Anonymous_GET_booking_returns_401", "GET", "/api/v1/bookings/{id}");
        yield return Anon("Anonymous_GET_my_bookings_returns_401", "GET", "/api/v1/bookings");
        yield return Anon("Anonymous_POST_booking_cancel_returns_401", "POST", "/api/v1/bookings/{id}/cancel");
        yield return Anon("Anonymous_POST_booking_confirm_returns_401", "POST", "/api/v1/bookings/{id}/confirm");
        yield return Anon("Anonymous_POST_booking_reject_returns_401", "POST", "/api/v1/bookings/{id}/reject");
        yield return Anon("Anonymous_POST_booking_checkin_returns_401", "POST", "/api/v1/bookings/{id}/check-in");
        yield return Anon("Anonymous_POST_booking_checkout_returns_401", "POST", "/api/v1/bookings/{id}/check-out");
        yield return Anon("Anonymous_POST_booking_complete_returns_401", "POST", "/api/v1/bookings/{id}/complete");
        yield return Anon("Anonymous_POST_booking_schedule_completion_returns_401", "POST", "/api/v1/bookings/{id}/schedule-completion");
        yield return Anon("Anonymous_POST_booking_review_returns_401", "POST", "/api/v1/bookings/{id}/review");

        // ---- Booking — admin list/detail + stubbed owner queue ----
        yield return Anon("Anonymous_GET_admin_bookings_returns_401", "GET", "/api/v1/admin/bookings");
        yield return Anon("Anonymous_GET_admin_booking_detail_returns_401", "GET", "/api/v1/admin/bookings/{id}");
        yield return Anon("Anonymous_GET_admin_bookings_queue_returns_401", "GET", "/api/v1/admin/bookings/queue");
        yield return Anon("Anonymous_POST_admin_bookings_manual_returns_401", "POST", "/api/v1/admin/bookings/manual");

        // ---- Booking — property calendar + owner blocks ----
        yield return Anon("Anonymous_GET_property_calendar_returns_401", "GET", "/api/v1/properties/{propertyId}/calendar");
        yield return Anon("Anonymous_GET_property_blocks_returns_401", "GET", "/api/v1/properties/{propertyId}/blocks");
        yield return Anon("Anonymous_POST_property_block_returns_401", "POST", "/api/v1/properties/{propertyId}/blocks");
        yield return Anon("Anonymous_DELETE_property_block_returns_401", "DELETE", "/api/v1/properties/{propertyId}/blocks/{blockId}");

        // ---- Pricing — owner plan + rules ----
        yield return Anon("Anonymous_GET_pricing_returns_401", "GET", "/api/v1/properties/{propertyId}/pricing");
        yield return Anon("Anonymous_PUT_pricing_returns_401", "PUT", "/api/v1/properties/{propertyId}/pricing");
        yield return Anon("Anonymous_POST_pricing_rule_returns_401", "POST", "/api/v1/properties/{propertyId}/pricing/rules");
        yield return Anon("Anonymous_PUT_pricing_rule_returns_401", "PUT", "/api/v1/properties/{propertyId}/pricing/rules/{ruleId}");
        yield return Anon("Anonymous_DELETE_pricing_rule_returns_401", "DELETE", "/api/v1/properties/{propertyId}/pricing/rules/{ruleId}");
        yield return Anon("Anonymous_PATCH_pricing_rule_enabled_returns_401", "PATCH", "/api/v1/properties/{propertyId}/pricing/rules/{ruleId}/enabled");
        yield return Anon("Anonymous_POST_pricing_rules_reorder_returns_401", "POST", "/api/v1/properties/{propertyId}/pricing/rules/reorder");

        // ---- Payment — booking payment intent + refunds ----
        yield return Anon("Anonymous_GET_payment_intent_for_booking_returns_401", "GET", "/api/v1/payments/intents/by-booking/{bookingId}");
        yield return Anon("Anonymous_POST_refund_returns_401", "POST", "/api/v1/payments/refunds");

        // ---- Reviews — owner response + admin moderation ----
        yield return Anon("Anonymous_POST_review_response_returns_401", "POST", "/api/v1/reviews/{id}/response");
        yield return Anon("Anonymous_GET_admin_reviews_returns_401", "GET", "/api/v1/admin/reviews");
        yield return Anon("Anonymous_POST_admin_review_hide_returns_401", "POST", "/api/v1/admin/reviews/{id}/hide");
        yield return Anon("Anonymous_POST_admin_review_restore_returns_401", "POST", "/api/v1/admin/reviews/{id}/restore");
        yield return Anon("Anonymous_POST_admin_review_reject_returns_401", "POST", "/api/v1/admin/reviews/{id}/reject");

        // ---- Sync — admin channel feeds + conflict resolution ----
        yield return Anon("Anonymous_GET_channel_feeds_returns_401", "GET", "/api/v1/admin/channel-feeds");
        yield return Anon("Anonymous_GET_channel_feed_returns_401", "GET", "/api/v1/admin/channel-feeds/{id}");
        yield return Anon("Anonymous_POST_channel_feed_returns_401", "POST", "/api/v1/admin/channel-feeds");
        yield return Anon("Anonymous_PUT_channel_feed_returns_401", "PUT", "/api/v1/admin/channel-feeds/{id}");
        yield return Anon("Anonymous_DELETE_channel_feed_returns_401", "DELETE", "/api/v1/admin/channel-feeds/{id}");
        yield return Anon("Anonymous_GET_sync_conflicts_returns_401", "GET", "/api/v1/admin/sync-conflicts");
        yield return Anon("Anonymous_POST_sync_conflict_resolve_returns_401", "POST", "/api/v1/admin/sync-conflicts/{id}/resolve");

        // ---- Notifications — admin log + retry ----
        yield return Anon("Anonymous_GET_admin_notifications_returns_401", "GET", "/api/v1/admin/notifications");
        yield return Anon("Anonymous_POST_admin_notification_retry_returns_401", "POST", "/api/v1/admin/notifications/{id}/retry");

        // ---- Messaging — threads + realtime negotiate ----
        yield return Anon("Anonymous_GET_threads_returns_401", "GET", "/api/v1/threads");
        yield return Anon("Anonymous_GET_thread_returns_401", "GET", "/api/v1/threads/{id}");
        yield return Anon("Anonymous_GET_thread_messages_returns_401", "GET", "/api/v1/threads/{id}/messages");
        yield return Anon("Anonymous_POST_thread_message_returns_401", "POST", "/api/v1/threads/{id}/messages");
        yield return Anon("Anonymous_POST_thread_read_returns_401", "POST", "/api/v1/threads/{id}/read");
        yield return Anon("Anonymous_POST_thread_attachment_returns_401", "POST", "/api/v1/threads/{id}/attachments");
        yield return Anon("Anonymous_GET_realtime_negotiate_returns_401", "GET", "/api/v1/realtime/negotiate");

        // ---- Admin — user search + stubbed toggles/alerts ----
        yield return Anon("Anonymous_GET_admin_users_returns_401", "GET", "/api/v1/admin/users");
        yield return Anon("Anonymous_GET_admin_toggles_returns_401", "GET", "/api/v1/admin/toggles");
        yield return Anon("Anonymous_PUT_admin_toggle_returns_401", "PUT", "/api/v1/admin/toggles/{key}");
        yield return Anon("Anonymous_GET_admin_alerts_returns_401", "GET", "/api/v1/admin/alerts");
        yield return Anon("Anonymous_POST_admin_alert_dismiss_returns_401", "POST", "/api/v1/admin/alerts/{id}/dismiss");

        // ---- Reports — owner/admin reporting surface ----
        yield return Anon("Anonymous_GET_report_occupancy_returns_401", "GET", "/api/v1/admin/reports/occupancy");
        yield return Anon("Anonymous_GET_report_revenue_returns_401", "GET", "/api/v1/admin/reports/revenue");
        yield return Anon("Anonymous_GET_report_adr_returns_401", "GET", "/api/v1/admin/reports/adr");
        yield return Anon("Anonymous_GET_report_source_returns_401", "GET", "/api/v1/admin/reports/source");

        // ---- Tenant — admin Stripe self-service: refresh-readiness ({tenantId}-gated) ----
        yield return Anon("Anonymous_POST_stripe_refresh_readiness_returns_401",
            "POST", "/api/v1/admin/tenants/{tenantId}/stripe/refresh-readiness");
        foreach (var (tenant, ownerPersona) in new[]
        {
            (TargetTenant.A, Persona.OwnerA),
            (TargetTenant.B, Persona.OwnerB),
        })
        {
            var oppositeOwner = ownerPersona == Persona.OwnerA ? Persona.OwnerB : Persona.OwnerA;
            yield return new Cell(
                $"Owner_{oppositeOwner}_POST_stripe_refresh_readiness_cross_tenant_{tenant}_rejected",
                "POST", "/api/v1/admin/tenants/{tenantId}/stripe/refresh-readiness",
                oppositeOwner, tenant, Forbidden);
        }

        // ---- Platform — SeedMembership + admin-user seed (PlatformAdmin only) ----
        yield return Anon("Anonymous_POST_platform_tenant_membership_returns_401",
            "POST", "/api/v1/admin/platform/tenants/{tenantId}/memberships");
        yield return new Cell("OwnerA_POST_platform_tenant_membership_returns_403",
            "POST", "/api/v1/admin/platform/tenants/{tenantId}/memberships",
            Persona.OwnerA, TargetTenant.A, Forbidden, JsonBody(new { entraOid = "seed-oid", role = "tenant_admin" }));
        yield return new Cell("OwnerB_POST_platform_tenant_membership_returns_403",
            "POST", "/api/v1/admin/platform/tenants/{tenantId}/memberships",
            Persona.OwnerB, TargetTenant.A, Forbidden, JsonBody(new { entraOid = "seed-oid", role = "tenant_admin" }));

        yield return Anon("Anonymous_POST_platform_user_seed_returns_401", "POST", "/api/v1/admin/platform/users/seed");
        yield return OwnerRejected("OwnerA_POST_platform_user_seed_returns_403", "POST", "/api/v1/admin/platform/users/seed", Persona.OwnerA);
        yield return OwnerRejected("OwnerB_POST_platform_user_seed_returns_403", "POST", "/api/v1/admin/platform/users/seed", Persona.OwnerB);
    }
}
