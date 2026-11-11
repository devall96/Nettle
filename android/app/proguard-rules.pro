# kotlinx.serialization keeps its generated serializers via its own consumer rules.
-dontwarn org.xmlpull.v1.**
-dontwarn org.kxml2.io.**
-keep class org.xmlpull.v1.** { *; }

# Belt and braces for kotlinx.serialization (its consumer rules already cover the usual cases):
# keep the generated serializer lookups on Nettle's own @Serializable models.
-keepclassmembers @kotlinx.serialization.Serializable class io.github.devall96.nettle.** {
    *** Companion;
    *** INSTANCE;
    kotlinx.serialization.KSerializer serializer(...);
}
-keepclasseswithmembers class io.github.devall96.nettle.** {
    kotlinx.serialization.KSerializer serializer(...);
}

# Keep real class, method and file names (the code is open source anyway), so the "Nettle closed
# unexpectedly" screen and logcat show where an error happened instead of "a.b.c". Unused code is
# still removed and the rest still optimised.
-dontobfuscate
-keepattributes SourceFile,LineNumberTable
