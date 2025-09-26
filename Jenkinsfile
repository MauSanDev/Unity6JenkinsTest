pipeline {
    agent {
        label 'unity-agent-1'  // Unity 6000.0.58f1
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
    }

    environment {
        UNITY_PROJECT_PATH = "${WORKSPACE}"
    }

    stages {
        stage('Checkout') {
            steps {
                echo "Checking out Unity 6 project..."
                checkout scm
            }
        }

        stage('Force Clean') {
            when {
                params.FORCE_CLEAN_BUILD == true
            }
            steps {
                echo "⚠️  FORCE CLEAN: This will significantly slow down the build!"
                sh 'rm -rf Library'
                echo "Library folder cleaned (expect slower build time)"
            }
        }

        stage('Unity License') {
            steps {
                script {
                    withCredentials([usernamePassword(credentialsId: 'unity-credentials',
                                                   usernameVariable: 'UNITY_USERNAME',
                                                   passwordVariable: 'UNITY_PASSWORD')]) {
                        sh '''
                            unity-editor \
                                -batchmode \
                                -quit \
                                -logFile /dev/stdout \
                                -username "$UNITY_USERNAME" \
                                -password "$UNITY_PASSWORD"
                        '''
                    }
                }
            }
        }

        stage('Build Android') {
            when {
                anyOf {
                    params.BUILD_TARGET == 'Android'
                    params.BUILD_TARGET == 'Both'
                }
            }
            steps {
                script {
                    def buildVersion = params.BUILD_VERSION ?: "1.0.${BUILD_NUMBER}"
                    def commitHash = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()

                    sh """
                        unity-editor \\
                            -batchmode \\
                            -quit \\
                            -projectPath "${UNITY_PROJECT_PATH}" \\
                            -executeMethod BuildScript.BuildBatchMode \\
                            -logFile /dev/stdout \\
                            -buildTarget Android \\
                            -buildVersion "${buildVersion}" \\
                            -buildSuffix "jenkins" \\
                            -commitHash "${commitHash}" \\
                            -buildId "${BUILD_NUMBER}" \\
                            -generateAddressables ${params.GENERATE_ADDRESSABLES} \\
                            -developmentBuild ${params.DEVELOPMENT_BUILD}
                    """
                }
            }
        }

        stage('Build WebGL') {
            when {
                anyOf {
                    params.BUILD_TARGET == 'WebGL'
                    params.BUILD_TARGET == 'Both'
                }
            }
            steps {
                script {
                    def buildVersion = params.BUILD_VERSION ?: "1.0.${BUILD_NUMBER}"
                    def commitHash = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()

                    sh """
                        unity-editor \\
                            -batchmode \\
                            -quit \\
                            -projectPath "${UNITY_PROJECT_PATH}" \\
                            -executeMethod BuildScript.BuildBatchMode \\
                            -logFile /dev/stdout \\
                            -buildTarget WebGL \\
                            -buildVersion "${buildVersion}" \\
                            -buildSuffix "jenkins" \\
                            -commitHash "${commitHash}" \\
                            -buildId "${BUILD_NUMBER}" \\
                            -generateAddressables ${params.GENERATE_ADDRESSABLES} \\
                            -developmentBuild ${params.DEVELOPMENT_BUILD}
                    """
                }
            }
        }
    }

    post {
        always {
            // Archive build artifacts and reports
            archiveArtifacts artifacts: '**/Development/**/*', allowEmptyArchive: true
            archiveArtifacts artifacts: '**/QA/**/*', allowEmptyArchive: true
            archiveArtifacts artifacts: '**/Release/**/*', allowEmptyArchive: true
            archiveArtifacts artifacts: '**/BuildReport.json', allowEmptyArchive: true
            archiveArtifacts artifacts: '**/BuildParameters.json', allowEmptyArchive: true

            echo "Build completed on Unity 6 persistent agent with Builder system"
        }
        success {
            echo "✅ Build successful! Check organized build folders and reports."
        }
        failure {
            echo "❌ Build failed - check logs above and BuildReport.json for details"
        }
    }
}