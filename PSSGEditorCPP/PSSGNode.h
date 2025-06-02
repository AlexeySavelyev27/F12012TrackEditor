#pragma once
#include <string>
#include <vector>
#include <unordered_map>

struct PSSGNode {
    std::string name;
    std::unordered_map<std::string, std::vector<unsigned char>> attributes;
    std::vector<PSSGNode> children;
    std::vector<unsigned char> data;

    // computed on save
    uint32_t attr_block_size = 0;
    uint32_t node_size = 0;
};
