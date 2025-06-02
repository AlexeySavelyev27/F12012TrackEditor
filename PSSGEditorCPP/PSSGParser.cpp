#include "PSSGParser.h"
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <zlib.h>

PSSGParser::PSSGParser(const std::string& path) : path_(path) {}

PSSGNode PSSGParser::Parse() {
    // TODO: implement full parser; placeholder loads empty root
    PSSGNode root;
    root.name = "Root";
    return root;
}

PSSGNode PSSGParser::ReadNode(std::istream& stream) {
    // TODO: implement
    return PSSGNode();
}
