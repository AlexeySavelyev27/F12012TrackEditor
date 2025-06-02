#pragma once
#include "PSSGNode.h"
#include "PSSGSchema.h"
#include <string>
#include <vector>

class PSSGParser {
public:
    PSSGParser(const std::string& path);
    PSSGNode Parse();
private:
    PSSGNode ReadNode();
    uint32_t ReadUInt();
    std::vector<unsigned char> ReadBytes(size_t count);
    void Ensure(size_t count);

    std::vector<unsigned char> data_;
    size_t pos_ = 0;
    PSSGSchema schema_;
    std::string path_;
};
