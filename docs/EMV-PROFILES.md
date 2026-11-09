# EMV tag and scheme profiles

The EMV Tag Decoder provides 13 tag decoders and 19 explicit tag/profile combinations. Choose the tag and then **Scheme / profile**. Only supported profiles appear for that tag. Common definitions are labelled **EMV contact 4.3 (common)**; this does not assert that every scheme or later kernel uses identical definitions.

Changing profile preserves the input hex, revalidates its length, updates the byte legends and removes stale results. **Load example** supplies a valid value for the current profile. The selected profile and its reference are shown above the results. Byte tabs support toggling fixed and variable-length bit values; coded multi-bit fields have a separate field summary. Individual code bits are not independent capability flags. CVM List is decoded into ordered rules instead of hundreds of byte toggles.

| Tag | Decoder | Supported profiles |
| --- | --- | --- |
| 9F33 | Terminal Capabilities | EMV contact 4.3 common |
| 95 | Terminal Verification Results | EMV contact 4.3 common |
| 9B | Transaction Status Information | EMV contact 4.3 common |
| 82 | Application Interchange Profile | Common; American Express / Kernel 4 v2.10 |
| 9F40 | Additional Terminal Capabilities | EMV contact 4.3 common |
| 9F07 | Application Usage Control | EMV contact 4.3 common |
| 9F27 | Cryptogram Information Data | EMV contact 4.3 common |
| 9F34 | CVM Results | Common; American Express mobile CVM / Kernel 4 v2.10 |
| 8E | CVM List | Common; American Express mobile CVM / Kernel 4 v2.10 |
| 9F35 | Terminal Type | EMV contact 4.3 common |
| 9F66 | Terminal Transaction Qualifiers | Visa / Kernel 3; Discover / Kernel 6 v2.7 |
| 9F6C | Card Transaction Qualifiers | Visa / Kernel 3 |
| 9F6E | Form Factor / Third Party Data / Reader Capabilities | Visa FFI version 1 (VCPS 2.2); Mastercard Third Party Data; American Express / Kernel 4 v2.10 |

## Form Factor examples

- **Visa FFI v1:** `23820000` identifies version `001`, consumer mobile phone, passcode and biometric capability, and proximity/NFC technology. It must be four bytes. Unknown FFI versions retain raw bytes and suppress version-1 feature meanings.
- **Mastercard Third Party Data:** `02800000323100` contains country code `0280`, identifier `0000`, ASCII device code `21` (hex `3231`, Mobile phone), and proprietary byte `00`. No country name is guessed: scheme country-code tables can contain legacy codes. Length is 5–32 bytes. If the identifier's high bit is clear, two device-type bytes and at least one proprietary byte are required (at least seven bytes total). With that bit set, the device field is absent. Unknown device codes/proprietary data remain raw.
- **American Express Kernel 4 v2.10:** `DCE00003` is the four-byte Enhanced Contactless Reader Capabilities structure. Its byte meanings differ from both Visa FFI and Mastercard Third Party Data. This is the pinned 2021 profile, not the later 2026 update.

## TLV context

BER-TLV / Field 55 has its own context selector: common-only, Visa, Mastercard Third Party Data, American Express Kernel 4 v2.10, or Discover Kernel 6 v2.7. Common-only decodes common tags and preserves ambiguous scheme tags without inferring a scheme from length or AID. An explicit context applies only matching supported profiles, with common definitions used for common tags. Unsupported scheme-specific tags remain raw.

## Sources and scope

- EMV contact definitions follow EMV 4.3 Book 3 Annex C and Book 4 Annex A, including CVM rules/results, CID, AUC, terminal type and terminal capabilities. The [EMVCo document catalogue](https://www.emvco.com/emv-technologies/emv-contactless-chip/) supplies the kernel specification family. Profiles are intentionally versioned rather than labelled as the latest specification.
- Visa FFI version-1 form codes, feature bits and technology coding follow Visa VCPS 2.2; Visa TTQ/CTQ follow the Kernel 3 / VCPS definitions. The linked [Form Factor reference tool](https://paymentcardtools.com/emv-tag-decoders/form-factor) was used to compare the Visa and Mastercard example workflows.
- Mastercard Third Party Data uses the M/Chip field structure. Device descriptions are grounded in [Visa Acceptance Solutions' documented Mastercard initiation-channel codes](https://developer.visaacceptance.com/docs/cybs/en-us/api-fields/reference/all/rest/api-fields/payment-info-aa/payment-info-initiation-channel.html), which explicitly map to 9F6E. The optional-device layout is also documented by the [CPACE terminal-kernel specification, section 23.27](https://www.europeancardpaymentassociation.com/wp-content/uploads/2018/07/CPACE-Terminal-Kernel-Specification_V1.0-20180712.pdf).
- American Express mappings are pinned to EMV Contactless Book C-4 v2.10, including ECRC table 4-4, AIP table 5-2 and contactless mobile CVM. Discover TTQ is pinned to Book C-6 v2.7. Later kernels, bulletins, issuer-defined CVMs and additional schemes require their own definitions; they are not silently substituted with one of these profiles.

Issuer Application Data (9F10), issuer-specific CVR/CVN layouts, UnionPay/JCB profiles and automatic AID-based profile selection are not implemented. Raw TLV values remain available for unsupported tags.
