plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

// Номер сборки — из GitHub Actions (каждая сборка новее предыдущей, обновление ставится поверх).
val buildNumber = (System.getenv("GITHUB_RUN_NUMBER") ?: "1").toInt()

android {
    namespace = "com.smartwarehouse.app"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.smartwarehouse.app"
        minSdk = 24          // Android 7.0 — подходит и для старых ТСД
        targetSdk = 35
        versionCode = buildNumber
        versionName = "1.0.$buildNumber"
    }

    // Ключ подписи — в репозитории (приложение внутреннее, репозиторий закрытый).
    // Один и тот же ключ у всех сборок — новая версия ставится поверх старой без удаления.
    signingConfigs {
        create("internal") {
            storeFile = file("smartwarehouse.keystore")
            storePassword = "smartwarehouse"
            keyAlias = "smartwarehouse"
            keyPassword = "smartwarehouse"
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("internal")
        }
        debug {
            signingConfig = signingConfigs.getByName("internal")
        }
    }

    buildFeatures {
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("androidx.swiperefreshlayout:swiperefreshlayout:1.1.0")
    // Сканер штрих- и QR-кодов камерой от Google (сервисы Google Play): своё окно камеры, разрешение на камеру не нужно.
    implementation("com.google.android.gms:play-services-code-scanner:16.1.0")
}
