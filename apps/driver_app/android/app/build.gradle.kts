import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// RAPPORT DE PLANTAGE, APPLIQUE SEULEMENT SI LE PROJET FIREBASE EXISTE.
//
// google-services.json se telecharge depuis la console Firebase et se place
// dans android/app/. IL N'EST PAS SECRET — il se lit dans n'importe quel APK
// publie, il ne contient que des identifiants de projet — et il DOIT etre
// versionne : sans lui, un depot fraichement clone compile sans rapport de
// plantage, en silence, ce qui est le pire des deux mondes.
//
// LA CONDITION EST TEMPORAIRE. Le jour ou le fichier sera la et versionne,
// ces deux lignes pourront devenir inconditionnelles — et il vaudra mieux
// qu'elles le deviennent, pour que son absence casse bruyamment.
val rapportDePlantage = file("google-services.json").exists()

if (rapportDePlantage) {
    apply(plugin = "com.google.gms.google-services")
    apply(plugin = "com.google.firebase.crashlytics")
} else {
    logger.warn(
        "HBA : android/app/google-services.json est absent. L'application se " +
            "construira SANS rapport de plantage. Voir apps/driver_app/README.md, " +
            "section Rapport de plantage."
    )
}

// CLE GOOGLE MAPS. Elle vit dans android/local.properties, que git ignore :
// une cle d'API poussee sur un depot est aspiree en quelques heures par les
// robots qui scrutent GitHub, et la facturation, elle, est bien reelle.
//
// Absente, la valeur reste vide : la carte s'affiche en gris et le journal
// dit « API key not found ». C'est volontairement bruyant plutot que muet.
val proprietesLocales = Properties().apply {
    val fichier = rootProject.file("local.properties")
    if (fichier.exists()) fichier.inputStream().use { load(it) }
}
val cleMaps: String = proprietesLocales.getProperty("googleMaps.apiKey") ?: ""

// SIGNATURE DE PUBLICATION. Le magasin refuse un paquet signe avec les cles de
// debogage — et le refuserait tard, apres televersement. Les identifiants du
// magasin de cles vivent dans android/key.properties, que git ignore deja au
// meme titre que *.jks : un magasin de cles perdu ou divulgue ne se remplace
// pas, il condamne la ligne de mise a jour de l'application.
//
// ABSENT, ON RETOMBE SUR LE DEBOGAGE ET ON LE DIT FORT. C'est ce qui permet a
// quiconque de lancer « flutter run --release » sans detenir le magasin de
// cles ; l'avertissement est la pour qu'on ne decouvre pas la chose le jour du
// televersement.
val proprietesCle = Properties().apply {
    val fichier = rootProject.file("key.properties")
    if (fichier.exists()) fichier.inputStream().use { load(it) }
}
val cleDePublication = proprietesCle.getProperty("storeFile") != null

android {
    namespace = "com.hbatechettrade.hba_driver"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // L'identifiant est definitif : il ne se change plus apres la
        // premiere publication sans perdre les installations existantes.
        applicationId = "com.hbatechettrade.hba_driver"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName

        manifestPlaceholders["googleMapsApiKey"] = cleMaps
    }

    signingConfigs {
        if (cleDePublication) {
            create("publication") {
                keyAlias = proprietesCle.getProperty("keyAlias")
                keyPassword = proprietesCle.getProperty("keyPassword")
                storeFile = file(proprietesCle.getProperty("storeFile"))
                storePassword = proprietesCle.getProperty("storePassword")
            }
        }
    }

    buildTypes {
        release {
            signingConfig = if (cleDePublication) {
                signingConfigs.getByName("publication")
            } else {
                logger.warn(
                    "HBA : android/key.properties est absent. La version release sera " +
                        "signee avec les cles de DEBOGAGE et le Play Store la refusera. " +
                        "Voir apps/driver_app/README.md, section Publier."
                )
                signingConfigs.getByName("debug")
            }

            // REDUCTION DU CODE : PRETE, MAIS PAS ACTIVEE, et c'est un choix.
            //
            // Elle allegerait le paquet — que le livreur paie au
            // telechargement, sur un forfait a la recharge. Mais R8 casse par
            // reflexion, donc UNIQUEMENT EN RELEASE, sur le telephone de
            // quelqu'un d'autre, et jamais pendant le developpement. Les
            // regles sont ecrites dans proguard-rules.pro ; les passer a true
            // se fait en deux lignes, le jour ou une version de release aura
            // ete essayee de bout en bout sur un vrai telephone.
            isMinifyEnabled = false
            isShrinkResources = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro",
            )
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}
