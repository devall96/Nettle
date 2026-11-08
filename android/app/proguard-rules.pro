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
