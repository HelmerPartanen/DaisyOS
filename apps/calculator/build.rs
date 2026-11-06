fn main() {
    slint_build::compile_with_config(
        "ui/calculator.slint",
        slint_build::CompilerConfiguration::new()
            .embed_resources(slint_build::EmbedResourcesKind::EmbedFiles),
    )
    .expect("Daisy Calculator UI");
}
