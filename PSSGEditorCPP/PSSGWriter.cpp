#include "PSSGWriter.h"
#include <fstream>

PSSGWriter::PSSGWriter(const PSSGNode& root) : root_(root) {}

void PSSGWriter::Save(const std::string& path) {
    std::ofstream file(path, std::ios::binary);
    if(!file) return; // TODO: error handling
    // TODO: implement full writer
}

void PSSGWriter::WriteNode(std::ostream& stream, const PSSGNode& node) {
    // TODO: implement
}
