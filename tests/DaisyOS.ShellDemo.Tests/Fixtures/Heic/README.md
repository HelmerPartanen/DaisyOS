# HEIC wallpaper fixtures

Generated from two 64x64 solid-color images (#e0a020 and #102060), encoded with
libheif/x265, and annotated using libheif's XMP API. These synthetic images
contain no external artwork. Frame zero is gold and frame one is dark blue.

- appearance.heic: Apple apr metadata, light=0 and dark=1.
- timed.heic: Apple h24 metadata, frame 0 from 06:00, frame 1 from 18:00.
- solar.heic: Apple solar metadata, high southern sun=0, below-horizon north=1.
- static.heif: no dynamic metadata, so the primary image must be used.

The binary property lists are generated with Python plistlib. Metadata is stored
as an XMP attribute, complementing the element form in the real Dynamic.heic
manual fixture. Fixtures are tiny and decoded by the production libheif path.
