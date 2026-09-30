import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// La cle Maps vient de local.properties, hors du depot. Absente, on injecte une
// chaine vide : la compilation passe et la carte s'affiche grise, ce qui est un
// symptome lisible — alors qu'un placeholder non resolu ferait echouer la
// fusion du manifeste avec une erreur qui ne nomme pas la cause.
val mapsApiKey: String = Properties().apply {
    val file = rootProject.file("local.properties")
    if (file.exists()) {
        file.inputStream().use { load(it) }
    }
}.getProperty("MAPS_API_KEY") ?: ""

// SIGNATURE DE PUBLICATION. Le bloc « release » etait reste celui du gabarit
// Flutter : « TODO: Add your own signing config », et une signature avec les
// cles de DEBOGAGE. Ces cles sont publiques et identiques sur toutes les
// machines de developpement — n'importe qui pouvait donc produire un paquet que
// le telephone accepterait comme une mise a jour legitime de HBA Client, et le
// Play Store, lui, refuse l'envoi. L'application livreur etait deja faite ;
// celle-ci ne l'etait pas (constat B7 du 30/09/2026).
//
// Les identifiants du magasin de cles vivent dans android/key.properties, que
// android/.gitignore ecarte deja au meme titre que *.jks et *.keystore —
// verifie. Un magasin de cles divulgue laisse publier a votre place ; un
// magasin de cles PERDU est pire, il condamne definitivement la ligne de mise a
// jour.
//
// ABSENT, ON RETOMBE SUR LE DEBOGAGE ET ON LE DIT FORT. C'est ce qui permet a
// quiconque de lancer « flutter run --release » sans detenir le magasin ;
// l'avertissement est la pour qu'on ne decouvre pas la chose le jour du
// televersement.
val proprietesCle = Properties().apply {
    val fichier = rootProject.file("key.properties")
    if (fichier.exists()) fichier.inputStream().use { load(it) }
}
val cleDePublication = proprietesCle.getProperty("storeFile") != null

android {
    namespace = "com.example.hba_client"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.example.hba_client"
        manifestPlaceholders["MAPS_API_KEY"] = mapsApiKey
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
                        "Voir apps/client_app/README.md, section Publier."
                )
                signingConfigs.getByName("debug")
            }
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
