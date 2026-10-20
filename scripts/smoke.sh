#!/usr/bin/env bash
# End-to-end smoke test against a running API (Development: Otp:ExposeCodeInResponse=true).
# Usage: API=http://localhost:5080 ADMIN_EMAIL=... ADMIN_PASSWORD=... scripts/smoke.sh
set -euo pipefail
API=${API:-http://localhost:5080}
V=$API/api/v1
J='Content-Type: application/json'
req() { local m=$1 u=$2 t=${3:-} d=${4:-}; curl -sS -X "$m" "$u" -H "$J" ${t:+-H "Authorization: Bearer $t"} -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" ${d:+-d "$d"}; }
login() { local phone=$1 role=$2 code
  code=$(req POST "$V/auth/otp/request" "" "{\"phone\":\"$phone\"}" | jq -r .devCode)
  req POST "$V/auth/otp/verify" "" "{\"phone\":\"$phone\",\"code\":\"$code\",\"role\":\"$role\",\"deviceId\":\"smoke\"}" | jq -r .accessToken; }
ok() { echo "✔ $*"; }

SUF=$RANDOM
C=$(login "0770$SUF$((RANDOM%900+100))" customer); ok "customer login"
req PUT "$V/me/profile" "$C" '{"name":"زبون تجربة","governorateId":1,"area":"الكرادة"}' >/dev/null; ok "profile"
P=$(login "0780$SUF$((RANDOM%900+100))" page); ok "page login"
PAGE=$(req POST "$V/pages/register" "$P" "{\"name\":\"متجر $SUF\",\"instagram\":\"shop$SUF\",\"categoryIds\":[2],\"governorateIds\":[1],\"acceptTerms\":true}" | jq -r .id); ok "page registered $PAGE"
A=$(req POST "$V/auth/admin/login" "" "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .accessToken)
req POST "$API/api/admin/v1/pages/$PAGE/verify" "$A" >/dev/null; ok "admin verified page"
P=$(curl -sS -X POST "$V/auth/switch-role" -H "$J" -H "Authorization: Bearer $P" -d '{"role":"page"}' | jq -r .accessToken)

IMG=$(curl -sS -X POST "$V/media" -H "Authorization: Bearer $C" -F kind=requests -F "file=@$(dirname "$0")/../src/Dawwerli.Infrastructure/Persistence/Seed/media/shoes.webp" | jq -r .key); ok "upload $IMG"
RID=$(req POST "$V/requests" "$C" "{\"mode\":\"photo\",\"mediaKey\":\"$IMG\",\"description\":\"نايك اير فورس\",\"categoryId\":2,\"size\":\"42\",\"color\":\"أبيض\",\"condition\":\"new\",\"maxPrice\":60000,\"governorateId\":1,\"readyToBuy\":true}" | jq -r .id); ok "request $RID"
req POST "$V/requests" "$C" '{"mode":"text","description":"أريد مسدس رقم 07701234567","categoryId":2,"condition":"new","maxPrice":0,"governorateId":1,"readyToBuy":true}' | jq -c '{code,errors}'
req GET "$V/pages/me/inbox" "$P" | jq -c '.[0]|{title,offers,maxPrice}'
OIMG=$(curl -sS -X POST "$V/media" -H "Authorization: Bearer $P" -F kind=offers -F "file=@$(dirname "$0")/../src/Dawwerli.Infrastructure/Persistence/Seed/media/shoes.webp" | jq -r .key)
req POST "$V/requests/$RID/offers" "$P" "{\"price\":70000,\"deliveryHours\":24,\"condition\":\"new\",\"photoKey\":\"$OIMG\"}" | jq -c '{status,code}'
OID=$(req POST "$V/requests/$RID/offers" "$P" "{\"price\":55000,\"deliveryHours\":24,\"condition\":\"new\",\"note\":\"أصلي\",\"photoKey\":\"$OIMG\"}" | jq -r .id); ok "offer $OID"
req POST "$V/requests/$RID/offers" "$P" "{\"price\":50000,\"deliveryHours\":24,\"condition\":\"new\",\"photoKey\":\"$OIMG\"}" | jq -c '{status,code}'
req GET "$V/requests/$RID" "$C" | jq -c '{reachedPages,offerPrices,status}'
SEL=$(req POST "$V/requests/$RID/select" "$C" "{\"offerId\":\"$OID\"}"); echo "$SEL" | jq -c '{orderNo,pagePhone,whatsAppLink}'
ORD=$(echo "$SEL" | jq -r .orderId)
req GET "$V/pages/me/offers" "$P" | jq -c '.[0]|{state,orderNo,customerPhone}'
req POST "$V/orders/$ORD/contacted" "$P" >/dev/null; ok "contacted"
req POST "$V/orders/$ORD/delivery-answer" "$C" '{"answer":"received"}' >/dev/null; ok "received"
req POST "$V/orders/$ORD/rating" "$C" '{"stars":5,"tags":["نفس الصورة"],"comment":"ممتاز"}' >/dev/null; ok "rated"
req POST "$V/orders/$ORD/rating" "$C" '{"stars":5,"tags":[],"comment":""}' | jq -c '{status,code}'
req GET "$V/orders/$ORD" "$C" | jq -c '{status,received,steps:[.steps[].done]}'
req POST "$V/pages/me/topups" "$P" "{\"method\":\"ZainCash\",\"transactionRef\":\"TX$SUF$RANDOM\",\"amount\":25000,\"receiptKey\":\"$OIMG\"}" | jq -c '{status,amount}'
TID=$(req GET "$API/api/admin/v1/topups" "$A" | jq -r '.[0].id'); req POST "$API/api/admin/v1/topups/$TID/approve" "$A" >/dev/null; ok "topup approved"
req GET "$V/pages/me/wallet" "$P" | jq -c '{balance,freeUsed,ledger:[.ledger[].type]}'
req POST "$V/pages/me/subscriptions/early-access" "$P" | jq -c '{earlyAccessUntil}'
AD=$(req POST "$V/pages/me/ads" "$P" "{\"slot\":\"homeBanner\",\"title\":\"خصم الأحذية\",\"headline\":\"خصم 20٪\",\"discountPercent\":20,\"governorateIds\":[1],\"categoryIds\":[],\"startAt\":\"2020-01-01T00:00:00Z\",\"days\":3}" | jq -r .id)
req POST "$API/api/admin/v1/ads/$AD/approve" "$A" >/dev/null; ok "ad approved"
req GET "$V/home?governorateId=1" "$C" | jq -c '{firstName,ad:.ad.title,stories:(.stories|length),categories:(.categories|length),deals:(.deals|length)}'
req GET "$V/pages/featured?categoryId=2&governorateId=1" | jq -c '{categoryName,pages:(.pages|length)}'
req GET "$API/api/admin/v1/kpis" "$A" | jq -c '{requestsToday,selectionRate,activePages}'
ok "smoke done"
