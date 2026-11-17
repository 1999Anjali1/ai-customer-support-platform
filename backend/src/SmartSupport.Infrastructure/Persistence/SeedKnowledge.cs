namespace SmartSupport.Infrastructure.Persistence;

/// <summary>
/// Help-centre articles for the fictional operator "Sahab Telecom".
/// These are what the AI searches (RAG) before answering policy questions.
/// </summary>
internal static class SeedKnowledge
{
    public static readonly (string Title, string Category, string Language, string Content)[] Articles =
    [
        ("Roaming packs and prices", "Roaming", "en", """
            International roaming must be switched ON before travelling. Customers can turn it on in the app, by chatting with SmartSupport, or by dialling *100#. Roaming cannot be activated while the account has an overdue bill.

            GCC Roaming Pack: QAR 50 for 7 days, 5 GB data and 60 minutes, valid in Saudi Arabia, UAE, Kuwait, Bahrain and Oman.

            Travel Pack Plus: QAR 120 for 15 days, 15 GB data and 120 minutes, valid in 40 countries including the UK, India, Egypt, Turkey and the USA.

            Without a pack, pay-as-you-go roaming costs QAR 20 per MB of data and QAR 8 per minute. We strongly recommend buying a pack before travelling. Packs start when the customer first connects to a network abroad, not at the time of purchase.

            Unused pack data does not carry over. Customers receive an SMS when 80% and 100% of the pack is used.
            """),

        ("How to pay your bill", "Payments", "en", """
            Bills are issued on the 1st of every month and are due on the 15th.

            Payment options: the Sahab Telecom app (debit/credit card or Apple Pay), the website, any Sahab Telecom store, self-service kiosks in major malls, and bank transfer with the account number as reference. Payments made by card are reflected immediately; bank transfers can take up to 2 working days.

            Auto-pay: customers can save a card and enable auto-pay in the app so the bill is paid on the due date automatically.

            If a bill is not paid 15 days after the due date it becomes overdue. With an overdue bill, plan changes, roaming activation and new add-ons are blocked, and outgoing calls may be suspended after 30 days. Service is restored within 1 hour of payment.
            """),

        ("Changing your mobile plan", "Plans", "en", """
            Customers can upgrade or downgrade their plan at any time through the app or SmartSupport. The change is effective immediately.

            Charges are prorated: the next bill shows the old plan for the days before the change and the new plan for the remaining days of the month. There is no fee for changing plans.

            A plan change is not possible while the account has an overdue bill. Downgrades are allowed once per billing month. Data already used in the month counts towards the new plan's allowance.

            Current plans: Smart 50 (QAR 50, 5 GB), Smart 100 (QAR 100, 20 GB, 5G), Smart 150 (QAR 150, 50 GB, 5G, unlimited local minutes) and Smart Unlimited (QAR 250, unlimited data, 5G).
            """),

        ("eSIM activation", "Device", "en", """
            eSIM is available on all postpaid plans and is free of charge. Supported devices include iPhone XS and newer, Samsung Galaxy S20 and newer, and Google Pixel 4 and newer.

            To get an eSIM: request it in the app under Account > SIM > Switch to eSIM. A QR code is sent by email within 15 minutes. On the phone, go to Settings > Mobile Data > Add eSIM and scan the QR code. Keep Wi-Fi on during activation.

            The physical SIM stops working once the eSIM is activated. A QR code can only be used once; if it fails, request a new one. Customers transferring eSIM to a new phone must request a new QR code. Visiting a store with a valid QID is required if the account email is not verified.
            """),

        ("Slow mobile data troubleshooting", "Network", "en", """
            If mobile data is slow or not working, try these steps in order:
            1. Check the data balance in the app – speed is reduced to 256 kbps after the plan allowance is used.
            2. Turn Airplane mode on for 10 seconds, then off.
            3. Make sure Mobile Data is on and the APN is set to "sahab.internet".
            4. Restart the phone.
            5. Check 5G/4G is enabled in Settings > Mobile Network > Preferred network type.

            If the problem continues in one location only (e.g. at home), it may be a coverage issue: a support ticket should be raised with the exact area/zone and street number so the network team can investigate. Network tickets are normally answered within 24 hours.
            """),

        ("5G coverage in Qatar", "Network", "en", """
            Sahab Telecom 5G is available in Doha, Lusail, The Pearl, West Bay, Al Wakrah, Al Khor, Mesaieed, Education City and along major highways. Coverage inside some buildings may fall back to 4G+.

            5G is included at no extra cost on Smart 100, Smart 150 and Smart Unlimited. Smart 50 customers use 4G+. A 5G-capable phone is required.

            Indoor coverage problems at a specific address can be reported through a network ticket; engineers may offer a free indoor signal booster for eligible postpaid customers.
            """),

        ("Refunds, disputes and billing errors", "Billing", "en", """
            If a customer believes a charge is wrong, a Billing ticket must be raised with the bill month and the disputed item. The billing team reviews disputes within 3 working days.

            While a dispute is open, the disputed amount does not count towards an overdue balance. Approved corrections are applied as a credit on the next bill; cash refunds are only given when an account is closed.

            SmartSupport and agents cannot promise refunds or credits in chat. Premium SMS subscriptions can be blocked on request.
            """),

        ("Lost or stolen phone", "Account", "en", """
            If a phone is lost or stolen, the customer should block the SIM immediately by calling 111 or via a human agent in chat. Blocking is free and stops all charges on the line.

            A replacement SIM (or eSIM) can be collected from any store with a valid QID, costing QAR 25 (free for Smart 150 and Smart Unlimited). The number, plan and balance are kept.

            For stolen phones, customers should also report to the police and can ask us to block the handset IMEI.
            """),

        ("التجوال الدولي وباقات السفر", "Roaming", "ar", """
            يجب تفعيل خدمة التجوال الدولي قبل السفر من خلال التطبيق أو عبر المساعد الذكي SmartSupport أو بطلب الرمز ‎*100#‎. لا يمكن تفعيل التجوال إذا كانت هناك فاتورة متأخرة السداد.

            باقة تجوال الخليج: 50 ريال قطري لمدة 7 أيام، تشمل 5 جيجابايت و60 دقيقة في السعودية والإمارات والكويت والبحرين وعُمان.

            باقة السفر بلس: 120 ريال قطري لمدة 15 يوماً، تشمل 15 جيجابايت و120 دقيقة في 40 دولة.

            ننصح بشراء الباقة قبل السفر لأن التجوال بدون باقة يكلف 20 ريال لكل ميجابايت.
            """),

        ("طرق دفع الفاتورة", "Payments", "ar", """
            تصدر الفواتير في اليوم الأول من كل شهر ويكون موعد السداد في اليوم الخامس عشر.

            يمكن الدفع عبر تطبيق سحاب تيليكوم بالبطاقة أو Apple Pay، أو الموقع الإلكتروني، أو أي فرع، أو أجهزة الخدمة الذاتية في المراكز التجارية، أو التحويل البنكي باستخدام رقم الحساب كمرجع.

            إذا لم تُدفع الفاتورة خلال 15 يوماً من تاريخ الاستحقاق تصبح متأخرة، ويتم إيقاف تغيير الباقة وتفعيل التجوال حتى يتم السداد.
            """)
    ];
}
