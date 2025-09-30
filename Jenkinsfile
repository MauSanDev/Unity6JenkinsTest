pipeline {
    agent {
        label 'unity-6-android'  // Unity 6000.0.58f1
    }

    parameters {
        choice(
            name: 'BUILD_TARGET',
            choices: ['Android', 'WebGL', 'Both'],
            description: 'Platform to build for'
        )
        booleanParam(
            name: 'DEVELOPMENT_BUILD',
            defaultValue: false,
            description: 'Create development build'
        )
        booleanParam(
            name: 'GENERATE_ADDRESSABLES',
            defaultValue: true,
            description: 'Generate Addressable assets'
        )
        booleanParam(
            name: 'FORCE_CLEAN_BUILD',
            defaultValue: false,
            description: 'Force clean Library folder (troubleshooting only - slow!)'
        )
        string(
            name: 'BUILD_VERSION',
            defaultValue: '',
            description: 'Override build version (leave empty for auto: 1.0.BUILD_NUMBER)'
        )
        string(
            name: 'BUILD_SUFFIX',
            defaultValue: '',
            description: 'Build suffix identifier (leave empty for default: jenkins)'
        )
    }

    environment {
        UNITY_PROJECT_PATH = "${WORKSPACE}"
        UNITY_EMAIL = credentials('unity-email')
        UNITY_PASSWORD = credentials('unity-password')
        UNITY_SERIAL = credentials('unity-serial')
    }

    stages {
        stage('Checkout') {
            steps {
                echo "Checking out Unity 6 project..."
                checkout scm
            }
        }

        stage('Debug Credentials') {
            steps {
                echo "🔍 Verifying Unity credentials are loaded..."
                echo "Unity Email: ${UNITY_EMAIL}"
                echo "Unity Serial: ${UNITY_SERIAL}"
                echo "Unity Password: [MASKED - Jenkins will hide this automatically]"
                echo "Environment variables set for GameCI activation"
            }
        }

        stage('Force Clean') {
            when {
                expression { params.FORCE_CLEAN_BUILD == true }
            }
            steps {
                echo "⚠️  FORCE CLEAN: This will significantly slow down the build!"
                sh 'rm -rf Library'
                echo "Library folder cleaned (expect slower build time)"
            }
        }


        stage('Build Android') {
            when {
                anyOf {
                    expression { params.BUILD_TARGET == 'Android' }
                    expression { params.BUILD_TARGET == 'Both' }
                }
            }
            steps {
                script {
                    def buildVersion = params.BUILD_VERSION ?: "1.0.${BUILD_NUMBER}"
                    def buildSuffix = params.BUILD_SUFFIX ?: "jenkins"
                    def commitHash = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()

                    sh """
                        set -x
                        echo "🚀 Starting Android build..."
                        echo "Build Version: ${buildVersion}"
                        echo "Build Suffix: ${buildSuffix}"
                        echo "Commit Hash: ${commitHash}"
                        echo "Development Build: ${params.DEVELOPMENT_BUILD}"
                        echo "Generate Addressables: ${params.GENERATE_ADDRESSABLES}"

                        unity-editor \\
                            -batchmode \\
                            -quit \\
                            -projectPath "${UNITY_PROJECT_PATH}" \\
                            -executeMethod BuildScript.BuildBatchMode \\
                            -logFile /dev/stdout \\
                            -buildTarget Android \\
                            -buildVersion "${buildVersion}" \\
                            -buildSuffix "${buildSuffix}" \\
                            -commitHash "${commitHash}" \\
                            -buildId "${BUILD_NUMBER}" \\
                            -generateAddressables ${params.GENERATE_ADDRESSABLES} \\
                            -developmentBuild ${params.DEVELOPMENT_BUILD} 2>&1 | tee android-build.log || echo "Unity build completed with exit code \$?"
                    """
                }
            }
        }

        stage('Build WebGL') {
            when {
                anyOf {
                    expression { params.BUILD_TARGET == 'WebGL' }
                    expression { params.BUILD_TARGET == 'Both' }
                }
            }
            steps {
                script {
                    def buildVersion = params.BUILD_VERSION ?: "1.0.${BUILD_NUMBER}"
                    def buildSuffix = params.BUILD_SUFFIX ?: "jenkins"
                    def commitHash = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()

                    sh """
                        set -x
                        echo "🌐 Starting WebGL build..."
                        echo "Build Version: ${buildVersion}"
                        echo "Build Suffix: ${buildSuffix}"
                        echo "Commit Hash: ${commitHash}"
                        echo "Development Build: ${params.DEVELOPMENT_BUILD}"
                        echo "Generate Addressables: ${params.GENERATE_ADDRESSABLES}"

                        unity-editor \\
                            -batchmode \\
                            -quit \\
                            -projectPath "${UNITY_PROJECT_PATH}" \\
                            -executeMethod BuildScript.BuildBatchMode \\
                            -logFile /dev/stdout \\
                            -buildTarget WebGL \\
                            -buildVersion "${buildVersion}" \\
                            -buildSuffix "${buildSuffix}" \\
                            -commitHash "${commitHash}" \\
                            -buildId "${BUILD_NUMBER}" \\
                            -generateAddressables ${params.GENERATE_ADDRESSABLES} \\
                            -developmentBuild ${params.DEVELOPMENT_BUILD} 2>&1 | tee webgl-build.log || echo "Unity build completed with exit code \$?"
                    """
                }
            }
        }
    }

    post {
        always {
            // Archive Unity build logs
            archiveArtifacts artifacts: '*.log', allowEmptyArchive: true, fingerprint: true

            // Archive build artifacts and reports
            archiveArtifacts artifacts: '**/Development/**/*', allowEmptyArchive: true
            archiveArtifacts artifacts: '**/QA/**/*', allowEmptyArchive: true
            archiveArtifacts artifacts: '**/Release/**/*', allowEmptyArchive: true
            archiveArtifacts artifacts: '**/BuildReport.json', allowEmptyArchive: true
            archiveArtifacts artifacts: '**/BuildParameters.json', allowEmptyArchive: true

            echo "Build completed on Unity 6 persistent agent with Builder system"
            echo "📥 Log files archived: android-build.log, webgl-build.log (if generated)"
        }
        success {
            echo "✅ Build successful! Check organized build folders and reports."
        }
        failure {
            echo "❌ Build failed - check logs above and BuildReport.json for details"
        }
    }
}