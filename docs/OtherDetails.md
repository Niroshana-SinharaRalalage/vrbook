Staging API URLs
Base: https://ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io
Swagger UI: /swagger (301 → /swagger/index.html)
OpenAPI JSON: /swagger/v1/swagger.json (123 KB, 55 paths)
Health: /health/live, /health/ready
A1 implemented: /api/v1/me (GET/PUT/DELETE) — try it in Swagger, DevAuth will inject a synthetic owner
Notes
DevAuth is enabled on staging right now — every request authenticates as dev@vrbook.local (an owner+admin). No bearer token needed.
Swagger is enabled via Swagger__EnableInProduction=true (set imperatively just now).
The other 54 endpoints are stubs — A1 ships only /me. The rest 501/return placeholders until their owning agents (A2 Catalog, A3 Pricing, A4 Booking, etc.) land.

/api/v1/me returns 200 with a real user JSON. The full end-to-end works:


{
  "id":"e98aa66c-06c4-4e5f-83c9-523e211afd9b",
  "email":"dev@vrbook.local",
  "displayName":"Dev Owner",
  "isOwner":true,
  "isAdmin":true,
  "emailVerified":true,
  "createdAt":"2026-06-04T18:46:17.121744+00:00",
  "lastLoginAt":"2026-06-04T18:47:03.4754773+00:00"
}

Next major step
A2 (Catalog & Search) per the proposal — Property aggregate, search API, image storage, listing pages. Roughly 2–3 days of work. Ready to start when you are.

Want me to dive into A2 now, or fix the /health/ready Degraded first?


Real AirBnB feed — log into AirBnB host account → open the listing's Calendar → Availability → Sync calendars → Export calendar → copy. The URL looks like https://www.airbnb.com/calendar/ical/12345.ics?s=... (placeholder format the field hints at).

Outbound iCal (give this to AirBnB)
Copy URL
http://ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io/feeds/57d2e238edbe41ca99cfed32e60350b7.ics
How to subscribe AirBnB to this URL
Open the AirBnB host calendar for this listing.
Go to Availability → Sync calendars → Import calendar.
Paste the URL above. Name it “VrBook”.
AirBnB will block these dates on its side within 2–3 hours.
Inbound URL (you pasted this from AirBnB)
https://calendar.google.com/calendar/ical/niroshhh%40gmail.com/private-8180edeed869804b1fbf095a48ae7d27/basic.ics