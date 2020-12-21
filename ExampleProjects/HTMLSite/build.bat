echo off
set build_dir=%cd%
echo building site at %build_dir%

..\..\bin\Debug\net5.0\StaticSiteGenerator.exe --input "." --output "../../docs/ExampleHTMLSite"