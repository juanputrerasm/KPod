KPod MOD playback add-on
========================

KPod plays WAV files on its own. Tracker modules (.mod) need this add-on, which
is the official libopenmpt 0.8.9 release for 32-bit Windows.

To install
----------

Copy these five DLLs into the folder that holds KPod.exe:

    libopenmpt.dll
    openmpt-mpg123.dll
    openmpt-ogg.dll
    openmpt-vorbis.dll
    openmpt-zlib.dll

That is the whole installation. Nothing is written to the registry, nothing is
copied under your user profile, and no network connection is used. To uninstall,
delete the five files; KPod keeps working, minus MOD playback.

The DLLs are 32-bit, and so is KPod.exe. They are not interchangeable with the
64-bit or ARM64 libopenmpt releases.

If a file is missing or Windows refuses to load it, KPod says "MOD playback is
not installed" when you open a .mod entry and otherwise carries on normally.

Licenses
--------

License.libopenmpt.txt      libopenmpt itself
License.mpg123.txt          mpg123 decoder, with License.mpg123.Authors.txt
License.ogg.txt             libogg
License.Vorbis.txt          libvorbis
License.zlib.txt            zlib

Keep these files with the DLLs when you redistribute the add-on.
