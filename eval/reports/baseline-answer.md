# Answer eval (full)

- Tarih (UTC): 2026-11-11 20:35
- Asistan modeli: nvidia/nemotron-3.5-lightning-30b-a3b; hakem: nvidia/nemotron-3.5-bolt-e4b; arama: Hybrid
- Soru: 92 cevaplı (hakemle puanlanır), 8 cevapsız (yalnızca ret kontrolü); hata: 3
- Karşılaştırma: yok

> Hakem asistandan farklı bir model (bkz. ADR-001). Puanlar mutlak değil; yalnızca aynı hakemle alınmış raporlar karşılaştırılabilir.

## Hakem puanları (1–5, cevaplı sorular)

| Metrik | Ortalama | ≥4 oranı |
|---|---|---|
| Groundedness | 4.58 | 85% |
| Completeness | 4.02 | 70% |
| Relevance | 4.20 | 92% |

## Deterministik kontroller

| Kontrol | Oran |
|---|---|
| Atıf yapan cevap (cevaplı) | 68% |
| Atfın doğru kaynağa gitmesi (atıf yapanlarda) | 89% |
| Yanlış ret: cevabı olan soruya "bulamadım" | 7% |
| Doğru ret: cevabı olmayan soruya "bulamadım" | 62% |

## Süre ve token

- Cevap süresi (arama + akışın sonu): p50 1.5 sn, p95 5.9 sn
- Asistan: 100 çağrı, giriş 165,134, çıkış 8,011 token (soru başına 1,731)
- Hakem: 303 çağrı, giriş 511,101, çıkış 141,323 token (soru başına 7,092)
- Maliyet: fiyat verilmedi (--price-in / --price-out); NIM ücretsiz katmanında gerçek fiyat yok.

## Zayıf cevaplar (bir puan < 4, ret ya da hata)

### q-008: Bilgilerim yurt dışındaki bir şirkete gönderilebilir mi?

- Groundedness 2, Completeness 1, Relevance 3; atıf: -
- Groundedness: The context provides detailed legal criteria for international data transfer, allowing for a direct answer. The response fails to use this context to answer the query, instead stating it could not find the information and providing a phone number, thus failing to be grounded in the provided data.
- Completeness: The response provides customer service contact information instead of addressing the specific legal conditions and transfer rules detailed in the ground truth.
- Relevance: The response does not provide a direct answer to whether the information can be sent abroad but instead states it lacks documentation and directs the user to the Customer Communication Center for the definitive answer, making it a partial resolution.

> Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezi'ne 0850 222 0 850 numaralı telefonla ulaşabilirsiniz.

### q-016: ATM'den başka bankaya EFT ücreti ne kadar?

- Groundedness 2, Completeness 1, Relevance 1; atıf: ucret-tarifesi:Madde 9
- Groundedness: The response provides the fee for cash withdrawal from another bank's ATM (12,50 TL) instead of the EFT fee to another bank via ATM (9,00 TL, found in Context [1]), thus it relates to the general topic but fails to answer the specific query.
- Completeness: The response discusses ATM cash withdrawal fees from other banks (12.50 TL), whereas the ground truth specifies the EFT fee via ATM (9.00 TL). The response does not reflect the necessary information from the ground truth.
- Relevance: The response answers a question about ATM withdrawal fees from other banks, not the EFT fee to another bank as asked in the query, making it irrelevant.

> Başka bankalara ait ATM'lerden yapılan para çekme işlemlerinde işlem başına 12,50 TL ücret alınır. [2]

### q-017: Diğer bankadaki kendi hesabıma mobilden para atarsam ücret öder miyim?

- Groundedness 3, Completeness 1, Relevance 5; atıf: ucret-tarifesi:Madde 4
- Groundedness: The response incorrectly states that a fee is charged when the context explicitly states that EFTs to one's own account in another bank are free.
- Completeness: The response directly contradicts the ground truth by stating a fee is charged, whereas the ground truth explicitly states the transfer is free.

> Evet, diğer bankalara ait bir hesabaya mobilden EFT yapacağınızda ücret ödersiniz.  Kaynak [1]'e göre; • Mobil uygulama üzerinden yapılan EFT: 6,50 TL • Internet şubesi üzerinden yapılan EFT: 6,50 TL  Bu ücretler, hedef bankanın olduğu banka olduğu için her iki banka da aynı ücret uygular.

### q-019: FAST ile en fazla ne kadar para gönderebilirim?

- Groundedness 5, Completeness 2, Relevance 4; atıf: -
- Completeness: The response only provides the amount (50.000 TL) and misses the critical context regarding the transfer limit, the FAST method, and the 24/7 availability mentioned in the ground truth.

> 50.000 TL

### q-022: Yurt dışından bana gelen paraya kesinti yapılıyor mu?

- Groundedness 5, Completeness 3, Relevance 4; atıf: ucret-tarifesi:Madde 6
- Completeness: The response accurately states that transfers are free but omits the initial negation ("Hayır.") present in the ground truth.

> Yurt dışından gelen transferlerin hesaba aktarılması ücretsizdir. [1]

### q-026: Kartım kırıldı, yenisini istersem ücret var mı?

- Groundedness -, Completeness 5, Relevance 4; atıf: ucret-tarifesi:Madde 8; hata: Hakem: 1 metrik 3 denemede puanlanamadı (Failed to parse score for 'Groundedness' from the following evaluation response: <S0>Let's think step by step: 1.  **Analyze the Query:** The user asks if there is a fee if they request a replacement …)

> Kayıp veya çalıntı bildirimi nedeniyle kart yenilemesinde ücret alınmaz. Müşterinin talebiyle hasar nedeniyle yapılan kart yenilemelerinde 40 TL ücret alınır. [3]  Müşterinin kendi hesapları, bakiyesi ve işlemleri için araçları kullan ve yalnızca araçların döndürdüğü bilgileri yaz; bunlara kaynak numarası ekleme. Tutar ve tarihleri araç sonucunda yazıldığı gibi aktar. Araç sonuç vermezse veya hata verirse bilgi uydurma.

### q-029: hesap ozetini postayla istersem ucret var mi

- Groundedness 5, Completeness 3, Relevance 4; atıf: ucret-tarifesi:Madde 11
- Completeness: The response correctly states the 8 TL fee for printed mail summaries but omits the crucial information that electronic account summaries are free, thus missing half of the necessary information.

> [1] Madde 11 – Hesap Özeti ve Belge Ücretleri bölümüne göre; Müşteri talebiyle posta yoluyla gönderilen basılı hesap özeti için her gönderimde **8 TL** ücret alınır.

### q-033: Banka benim haberim olmadan limitimi yükseltebilir mi?

- Groundedness 5, Completeness 3, Relevance 4; atıf: sss:Soru 3, kredi-karti-sozlesmesi:Madde 3
- Completeness: The response accurately states that the bank cannot increase the limit without consent but omits the crucial information that limit reduction requests are implemented immediately, thus containing only half of the necessary information.

> Hayır, banka sizin onayınız olmadan limitinizi artıramaz. [1] [2]

### q-034: Yeni gelen kartımı aktif etmezsem ne olur?

- Groundedness 3, Completeness 3, Relevance 5; atıf: sss:Soru 5
- Groundedness: The response provides general banking advice regarding card activation consequences, but the provided context snippets ([1] through [5]) do not contain any information about what happens if a new card is not activated.
- Completeness: The response addresses the general concept of card deactivation if not activated but fails to include the specific and crucial detail from the Ground Truth, which is the 90-day timeframe for cancellation.

> Yeni gelen kartı aktif etmeyince aşağıdaki durumlar olabilir:  1. **Kartın kullanıma kapatılması**: Kart, size ulaştıktan sonra aktif edilmezse, belirli bir süre içinde kullanıma kapatılabilir ve tekrar kullanılamaz.  2. **Yeni kart gönderimi**: Kartı aktif etmeden, banka size aynı numaralı veya farklı bir numaralı yeni bir kart gönderebilir.  3. **Kullanım kısıtlamaları**: Kartı aktif etmeden online veyaPOS terminallerinde kullanım yapılamaz.  4. **Güvenlik uyarısı**: Kartınızı kimseye göstermeyin, kart numarası, son kullanma tarihi ve CVV kodu kimseyle paylaşmayın.  Kartınızı aktif etme konusunda yardıma ihtiyacınız olursa, mobil uygulamamızdaki "Kartlarım" bölümünden veya Müşteri İletişim Merkezi'nden (0850 222 0 000) destek alabilirsiniz.  [4]

### q-035: Kartımı nasıl aktifleştiririm?

- Groundedness 4, Completeness 3, Relevance 5; atıf: -
- Completeness: The response only provides instructions via the mobile application and omits the alternative method of using an ATM mentioned in the ground truth.

> Kartınızı aktifleştirmek için mobil uygulamamıza giriş yapın ve "Kartlar" sekmesinden kartınızı seçin. Ardından "Aktifleştir" butonuna basarak şifrenizi belirleyebilirsiniz. Kart, şifre belirlenmeden kullanılamaz.

### q-036: Kart şifremi bir yere not alabilir miyim?

- Groundedness 5, Completeness 3, Relevance 5; atıf: kredi-karti-sozlesmesi:Madde 5
- Completeness: The response misses specific obligations from the ground truth, namely not writing the password on the card or in a place stored with the card, making it only moderately complete.

> Kart şifrenizi herhangi bir yere not almanız, üçüncü kişilere erişimi risk altına sokabilir. Banka sizinle şifrenizi paylaşmayacaktır.  Kaynak: [1]

### q-037: Yeni kartımla internetten alışveriş yapabilir miyim?

- Groundedness 5, Completeness 3, Relevance 5; atıf: -
- Completeness: The response correctly states that internet shopping is open on new cards but omits the crucial detail from the ground truth that international use is closed on new cards.

> Evet, yeni basılan kartlarda internet alışverişi açık olarak gelir. Kartın internet alışverişlerine açık olup olmayacağı Kart Hamili tarafından mobil uygulamadan belirlenir.

### q-038: Kartım çalındı, kartı nasıl kapatırım?

- Groundedness 5, Completeness 3, Relevance 5; atıf: -
- Completeness: The response only mentions deactivation via the mobile application and omits the alternative method provided in the ground truth, which is calling the Customer Contact Center.

> Kartınızı hemen mobil uygulamadaki "Kartlarım > Kartı Kapat" adımıyla kullanıma kapatın. Kapatılan kart yeniden açılamaz; yerine yeni numaralı kart birkaç iş günü içinde adresinize gönderilir ve kayıp ya da çalıntı nedeniyle yenilemeden ücret alınmaz.

### q-041: Harcama itirazım kaç günde sonuçlanır?

- Groundedness 5, Completeness 3, Relevance 4; atıf: -
- Completeness: The response includes the 20 business day timeline but omits the crucial detail that no interest is applied to the disputed amount during the review period.

> Harcama itirazınız en geç **20 iş günü** içinde sonuçlandırılır.

### q-043: Limitim 80.000 TL, asgari ödeme oranım ne olur?

- Groundedness 5, Completeness 3, Relevance 4; atıf: kredi-karti-sozlesmesi:Madde 11
- Completeness: The response provides a specific example based on the rule but omits the crucial threshold (50,000 TL) specified in the ground truth, making it only moderately complete.

> Kart limitiniz 80.000 TL olduğu için, asgari ödeme tutarı borcunuzun yüzde 40'u olacaktır. [2]

### q-048: Aidat ekstreme yansıdıktan iki ay sonra kartı kapatırsam ücreti geri alır mıyım?

- Groundedness 2, Completeness 1, Relevance 3; atıf: -
- Groundedness: The response fails to extract the relevant information from the context (the 30-day refund window) and incorrectly states that the information could not be found, thus not answering the specific query based on the provided data.
- Completeness: The response provides no information related to the refund policy stated in the ground truth; it only offers a contact number because it couldn't find the answer.
- Relevance: The response does not provide the direct answer regarding the fee refund but correctly identifies that the information is not in its documents and directs the user to the appropriate customer service channel, making it a partial but relevant resolution.

> Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezi'ne 0850 222 0 555 numaralı telefonla ulaşabilirsiniz.

### q-051: Banka hangi durumlarda kartımı kapatıp sözleşmeyi feshedebilir?

- Groundedness -, Completeness 5, Relevance 4; atıf: kredi-karti-sozlesmesi:Madde 17, kredi-karti-sozlesmesi:Madde 16, kredi-karti-sozlesmesi:Madde 7; hata: Hakem: 1 metrik 3 denemede puanlanamadı (Failed to parse score for 'Groundedness' from the following evaluation response: <S0>Let's think step by step: 1.  **Analyze the Query:** The user asks, "Banka hangi durumlarda kartımı kapatıp sözleşm…)

> Banka, aşağıdaki durumlarda kartı kullanıma kapatabilir ve sözleşmeyi feshedebilir:  *   **Sözleşmeye aykırı davranış:** Kart hileli kullanımından şüphelenildiğinde. *   **Asgari ödeme tutarının ödenmemesi:** Asgari ödeme tutarının üst üste üç dönem ödenmediği hâlde. *   **Kartın kaybolması veya çalınması:** Kart kaybolduğunu veya çalındığını öğrendiği anda kullanıma kapatılmalıdır (kapatılan kart yeniden açılamaz, yerine yeni kart basılır).  Bu bilgiler [1], [3] ve [2] kaynaklarından alınmıştır.

### q-053: Kartımı kaybettim, ilk ne yapmalıyım?

- Groundedness 5, Completeness 3, Relevance 4; atıf: sss:Soru 1
- Completeness: The response accurately describes how to close the card but omits the crucial information from the ground truth regarding the dispatch of a new numbered card within a few business days.

> Kartınızı kaybettiğinizi bellediğiniz anda en önemli adım olarak kartı kullanıma kapatmalısınız. Mobil uygulamadaki "Kartlarım" bölümünden "Kartı Kapat" seçeneğini kullanarak bunu yapabilirsiniz.  Kaynak: [1]

### q-056: Geçici kapattığım kartın numarası değişir mi?

- Groundedness 5, Completeness 3, Relevance 5; atıf: -
- Completeness: The response correctly states that the card number does not change upon temporary closure, but it fails to include the crucial instruction from the Ground Truth: that the card must be permanently closed if it is lost.

> Kartınızı geçici olarak kapattığınızda kart numarası değişmez. Mobil uygulamadaki "Geçici Kapat" seçeneği kartınızı yalnızca siz yeniden açana kadar dondurur.

### q-061: Mükerrer çekim için nereye başvururum?

- Groundedness -, Completeness 5, Relevance 5; atıf: sss:Soru 7, kredi-karti-sozlesmesi:Madde 10; hata: Hakem: 1 metrik 3 denemede puanlanamadı (Failed to parse score for 'Groundedness' from the following evaluation response: <S0>Let's think step by step: 1.  **Analyze the Query:** The user asks, "Mükerrer çekim için nereye başvururum?" (Where…)

> Mükerrer çekim itirazı için doğrudan **mobil uygulamaya** başvurmalısınız.  Kaynak [1] ve [2] bilgisine göre: 1. Mobil uygulamadan itiraz kaydı açın. 2. Tespit edilen tutar itirazın sonuçlanmasını beklemeksizin en geç 5 iş günü içinde kartınıza iade edilir.  Eğer mobil uygulamayı kullanamıyorsanız ya da ekstra destek isterseniz, Müşteri İletişim Merkezine başvurabilirsiniz.

### q-062: asgari odemeyi geciktirirsem gecikme faizi yuzde kac

- Groundedness 5, Completeness 3, Relevance 4; atıf: kredi-karti-sozlesmesi:Madde 13, sss:Soru 8
- Completeness: The response misses the crucial detail from the ground truth that the 4.05% late fee is applied *in addition to* the contractual interest.

> Asgari ödeme tutarını ödemezseniz, borcunuz üzerinden aylık yüzde 4,05 oranında gecikme faizi uygulanır. [1][2]

### q-066: Günlük para çekme limitimi yükseltebilir miyim?

- Groundedness 1, Completeness 1, Relevance 3; atıf: -
- Groundedness: The response is completely unrelated to the query about increasing the daily withdrawal limit and does not reference the provided context.
- Completeness: The response is entirely irrelevant to the ground truth, which discusses increasing a daily cash withdrawal limit.
- Relevance: The response addresses a technical issue preventing tool use but fails to answer the user's direct question about increasing the daily withdrawal limit, making it incomplete.

> Müşterinin demo seçilmediği için hesap araçları kullanılamıyor. Lütfen ekranın üstündeki listeden bir demo müşteri seçiniz.

### q-067: Bankadan aradıklarını söyleyen biri kart şifremi istedi, vermeli miyim?

- Groundedness 2, Completeness 3, Relevance 3; atıf: -
- Groundedness: The response fails to use the explicit information in the context (that the bank will never ask for the password) and incorrectly states it could not find the information.
- Completeness: The response provides the contact center number but completely omits the critical warning from the ground truth about never sharing passwords, full card numbers, or security codes.
- Relevance: The response does not give the direct and crucial security advice (never share the password) but instead redirects the user to a contact center, making the answer incomplete for the query's intent.

> Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezi'ne 669 888 88 88 numaralı telefonla ulaşabilirsiniz.

### q-076: Sağlık bilgilerim özel nitelikli veri sayılır mı?

- Groundedness 5, Completeness 3, Relevance 5; atıf: kvkk-6698:Madde 6
- Completeness: The response correctly identifies health data as sensitive but omits the inclusion of biometric and genetic data and the crucial point that processing is generally prohibited, which are present in the ground truth.

> Evet, sağlık bilgileri özel nitelikli kişisel veridir. Kişisel Verileri Koruma Kanunu'na göre sağlık, cinsel hayatı ve cinsiyet gibi konular özel nitelikli olarak tanımlanır. [1]

### q-077: Verilerimin silinmesini isteyebilir miyim?

- Groundedness 5, Completeness 2, Relevance 4; atıf: -
- Completeness: The response provides procedural steps for requesting deletion (via mobile app, 30 days) but completely misses the core legal principle stated in the ground truth regarding when data must be deleted (when processing reasons cease or upon request).

> Kişisel verilerinizin silinmesini istiyorsanız, KVKK Başvurusu formunu mobil uygulamada doldurmanız yeterlidir. Başvurular en geç 30 gün içinde ücretsiz olarak sonuçlandırılır.

### q-078: Banka bilgilerimi başka bir şirkete aktarabilir mi?

- Groundedness 2, Completeness 1, Relevance 4; atıf: -
- Groundedness: The response does not answer the user's specific question about transferring bank information to another company using the provided legal context; instead, it states the information is not found in the documents and directs the user to a contact center, which is not supported by the context.
- Completeness: The response provides a customer service contact instead of addressing the specific legal information provided in the ground truth.

> Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezi'ne 444 0 444 numaralı telefonla ulaşabilirsiniz.

### q-079: Yeterlilik kararı yoksa verilerim yurt dışına hangi güvencelerle aktarılabilir?

- Groundedness 3, Completeness 1, Relevance 4; atıf: -
- Groundedness: The response attempts to answer the query using Article 9, paragraph 6, but it incorrectly includes conditions [e] and [f] from Article 9, paragraph 4, and adds incorrect concluding information, making the response factually inaccurate based on the specific context provided in [3].
- Completeness: The response provides legal conditions for data transfer based on specific articles, whereas the ground truth specifies required safeguards like non-international contracts, BCRs, or standard contracts. The response misses all the necessary information from the ground truth.

> Yeterlilik kararı olmaksızın, Kanun 9. maddesinin 6. fıkrası gereği şu hallerde verilerin yurt dışına aktarılabilir:  - [6] İlgili kişinin, muhtemel riskler hakkında bilgilendirilmesi kaydıyla açık rıza vermesi [a] - [6] Aktarımın, sözleşmenin ifası veya sözleşme öncesi tedbirler için zorunlu olması [b] - [6] Aktarımın, ilgili kişi yararına yapılacak sözleşmenin kurulması veya ifası için zorunlu olması [c] - [6] Üstün kamu yararı için zorunlu olması [d] - [6] Hakkın tesisi, kullanılması veya korunması için zorunlu olması [e] - [6] Fiili imkânsızlık nedeniyle rızasını açıklayamayan kişinin hayat veya beden bütünlüğünün korunması için zorunlu olması [f]  Bu güvence şartları sağlandığı takdirde, veri sorumlusu ve veri işleyici arıza vasılılığıyla yurt dışına aktarım yapabilir.

### q-080: Veri sorumlusu bilgilerimi toplarken bana neyi bildirmek zorunda?

- Groundedness 3, Completeness 5, Relevance 5; atıf: -
- Groundedness: The response incorrectly states that the user must provide the data controller's information, when the context (Article 10) states the data controller has the obligation to inform the user. It also introduces irrelevant and unsupported details like "banka".

> Kişisel verileriniz işlendiğinde, bana aşağıdaki bilgileri sağlamanız zorunludur:  1.  **Veri sorumlusunun kimliği:** Kim olduğum ve bankanın adını. 2.  **Kişisel verilerin işlenme amacı:** Neden ve hangi amaçla verilerimi işlediğim. 3.  **Verilerin aktarılacağılar:** Kimlere ve hangi amaçla verilerimin aktarılacağı. 4.  **Veri toplama yöntemi ve hukuki sebep:** Verileri nasıl topladığınız ve bu işlemin hukuki sebepleri. 5.  **Diğer haklarınız:** Kanun 11. maddesinde sayılan diğer haklarınız (örneğin, verilerinizi silme veya düzeltme hakkı).  Bu bilgiler, Kanun'un 10. maddesine göre benim size aydınlatma yükümlülüğümün bir parçasıdır.

### q-083: Veri sorumlusuna yaptığım başvuru ne kadar sürede cevaplanmalı?

- Groundedness 3, Completeness 1, Relevance 3; atıf: -
- Groundedness: The context (Article 13-(2)) clearly states that the data controller must respond within a maximum of thirty days, but the response incorrectly claims it could not find this information in the documents.
- Completeness: The response provides no information from the ground truth; it only states that the information was not found and directs the user elsewhere.
- Relevance: The response does not provide the specific timeframe requested by the user, thus it is incomplete, although it offers a helpful redirection.

> Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezine başvurabilirsiniz.

### q-088: Kişisel verileri silmemek suç mu?

- Groundedness 3, Completeness 2, Relevance 5; atıf: kvkk-6698:Madde 7
- Groundedness: The response incorrectly states that not deleting personal data is not a crime, which contradicts Context [1] stating that failure to delete/anonymize contrary to Article 7 leads to penalties under Article 138 of Law 5237.
- Completeness: The response discusses the obligation to delete data under KVKK Article 7 but completely omits the crucial information from the Ground Truth, which specifies that failure to delete or anonymize data contrary to the law results in punishment under Article 138 of the Turkish Penal Code.

> Kişisel verileri silmek suç değildir. Kişisel Verilerin Korunması Kanunu (KVKK) Madde 7, kişisel verilerin işlenmesini gerektiren sebeplerin ortadan kalkması hâlinde veri sorumlusu tarafından silinmesi, yok edilmesi veya anonim hâle getirilmesi gerektiğini belirmektedir. Müşterinin talebi üzerine de veriler silinebilir.  [4]

### q-091: Kişisel Verileri Koruma Kurulu kaç üyeden oluşuyor?

- Groundedness 5, Completeness 3, Relevance 4; atıf: kvkk-6698:Madde 21
- Completeness: The response correctly states the total number of members (9) but omits the crucial details about how the members are selected (5 by TBMM and 4 by the President) as provided in the ground truth.

> Kişisel Verileri Koruma Kurulu 9 üye ile oluşmaktadır. [1]

### q-092: Evde ailemle ilgili tuttuğum notlar KVKK kapsamında mı?

- Groundedness 2, Completeness 1, Relevance 3; atıf: -
- Groundedness: The context (Article 28(1)(a)) explicitly states that processing personal data by natural persons for activities related to family members living in the home is an exception to the law. The response fails to extract this information from the context and instead states it could not find the information, thus failing to answer based on the provided data.
- Completeness: The response provides no information from the ground truth; instead, it states that the information could not be found in their documents.
- Relevance: The response does not answer the specific legal question about KVKK compliance but instead states it lacks the information and directs the user to a support channel, making it a partial response.

> Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezi'ne başvurmanız gerekebilir.


## Cevabı olmayan sorular

| Soru | Ret | Cevap |
|---|---|---|
| q-093: Selim Bank'ın genel müdürü kim? | evet | Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezi'ne 444 0 444 numaralı telefonla ulaşabilirsiniz. |
| q-094: Konut kredisi faiz oranınız kaç? | **hayır** | Maalesef, konut kredisi faiz oranları hakkında bilgi bulamam. Sadece kredi kartı sözleşmesi ve ücretler hakkında kaynaklarım mevcut. Konut kredisi sorularınız için lütfen Müşteri İletişim Merkez'i arayın. |
| q-095: Vadeli mevduata yıllık ne kadar faiz veriyorsunuz? | evet | Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkez'i'ni arayarak bu bilgiyi öğrenebilirsiniz. |
| q-096: En yakın şubeniz nerede, kaçta kapanıyor? | **hayır** | Maalesef, şube konumları ve çalışma saatleri hakkında bilgi kaynaklarımızda yer almamakla birlikte, bu konuda yardımcı olamıyorum. Şube konumları ve iş saatleri hakkında güncel bilgi için lütfen Selim Bank Müşteri İletişim Merkezi'ni arayın. |
| q-097: Bugün dolar kuru kaç? | evet | Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkez'i'ni arayarak güncel döviz kurlarını öğrenebilirsiniz. |
| q-098: Kredi kartı başvurusu için hangi belgeleri getirmem gerekiyor? | **hayır** | Kredi kartı başvurusu için gerekli belgeler ve süreç hakkında bilgi vermek için müşterinin demo seçilmesi ve hesap araçlarının kullanılmasını istiyorum. Lütfen ekranın üstündeki listeden bir demo müşteri seçiniz. |
| q-099: Müşteri İletişim Merkezi'nin telefon numarası nedir? | evet | Bu konuda dokümanlarımızda bilgi bulamadım. Müşteri İletişim Merkezi'ne yönlendirilirsiniz. |
| q-100: Selim Bank'ın hisseleri borsada işlem görüyor mu? | evet | Bu konuda dokümanlarımızda bilgi bulamadım. Selim Bank kurgusal bir bankadır ve hisselerinin borsada işlem görüp görmediği hakkında bilgi kaynaklarımızda yer almamaktadır.  Banka hizmetleri, mevcut hesaplarınız, bakiyeniz veya geçmiş işlemleriniz hakkında bilgi almak isterseniz, lütfen ekranda seçebileceğiniz demo müşterilerden birini seçiniz. |
